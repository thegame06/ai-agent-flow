import { useRef, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { day, money, dateTime, waitingFor, promiseStatus, HANDOFF_STATE } from './shared';

import type { AttentionItem, CollectionPromise } from './shared';

// ----------------------------------------------------------------------

type ConversationMessage = {
  id: string;
  direction: 'in' | 'out';
  actor: 'customer' | 'assistant' | 'human';
  content: string;
  createdAt: string;
  status?: string | null;
};

type Conversation = {
  sessionId: string;
  channelId: string;
  recipient: string;
  displayName?: string | null;
  state?: string | null;
  reason?: string | null;
  assignee?: string | null;
  lastActivityAt: string;
  messages: ConversationMessage[];
  contactId?: string | null;
  campaignName?: string | null;
};

type CustomerContext = {
  party: { displayName?: string; fullName?: string; phone?: string } | null;
  loans: { id: string; currency: string; totalBalance: number; overdueBalance: number; daysPastDue: number }[];
  promises: CollectionPromise[];
};

type Props = {
  /** /api/v1/tenants/{t} */
  tenantBase: string;
  item: AttentionItem | null;
  onClose: () => void;
  onChanged: () => void;
  onOpenCustomer: (partyId: string) => void;
};

const ACTOR_LABEL: Record<ConversationMessage['actor'], string> = {
  customer: 'Cliente',
  assistant: 'Asistente',
  human: 'Asesor',
};

/** One conversation handed to a person: the chat, the customer's balance and the reply box. */
export function ConversationDrawer({ tenantBase, item, onClose, onChanged, onOpenCustomer }: Props) {
  const conversationsBase = `${tenantBase}/conversations`;
  const sessionId = item?.sessionId ?? null;

  const [conversation, setConversation] = useState<Conversation | null>(null);
  const [customer, setCustomer] = useState<CustomerContext | null>(null);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  const load = useCallback(() => {
    if (!sessionId) return;
    axios
      .get<Conversation>(`${conversationsBase}/${sessionId}`)
      .then((res) => setConversation(res.data))
      .catch(() => setError('No se pudo cargar la conversación.'));
  }, [conversationsBase, sessionId]);

  useEffect(() => {
    setConversation(null);
    setCustomer(null);
    setText('');
    setError(null);
    setNotice(null);
    load();
    if (item?.partyId) {
      axios
        .get<CustomerContext>(`${tenantBase}/collections/customers/${item.partyId}`)
        .then((res) => setCustomer(res.data))
        .catch(() => setCustomer(null));
    }
  }, [load, item, tenantBase]);

  // The customer keeps writing while the advisor reads: poll while the drawer is open.
  useEffect(() => {
    if (!sessionId) return undefined;
    const id = window.setInterval(load, 5000);
    return () => window.clearInterval(id);
  }, [load, sessionId]);

  const messageCount = conversation?.messages.length ?? 0;
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: 'end' });
  }, [messageCount]);

  const run = async (action: () => Promise<unknown>, done?: string) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
      if (done) setNotice(done);
      load();
      onChanged();
      return true;
    } catch (err: any) {
      setError(err?.response?.data?.error ?? err?.response?.data?.message ?? 'No se pudo completar la acción.');
      return false;
    } finally {
      setBusy(false);
    }
  };

  const take = () => run(() => axios.post(`${conversationsBase}/${sessionId}/take`));

  const release = async () => {
    if (await run(() => axios.post(`${conversationsBase}/${sessionId}/release`))) onClose();
  };

  const resolveContact = async () => {
    if (!item?.contactId) return;
    if (await run(() => axios.post(`${tenantBase}/campaign-contacts/${item.contactId}/attention/resolve`))) onClose();
  };

  const send = async () => {
    const content = text.trim();
    if (!content) return;
    const url = sessionId
      ? `${conversationsBase}/${sessionId}/reply`
      : `${conversationsBase}/contacts/${item?.contactId}/reply`;
    if (await run(() => axios.post(url, { content }), sessionId ? undefined : 'Mensaje enviado.')) setText('');
  };

  const state = conversation?.state ?? (sessionId ? 'assistant' : item?.state ?? 'flagged');
  const st = HANDOFF_STATE[state] ?? HANDOFF_STATE.flagged;
  const name =
    customer?.party?.displayName || customer?.party?.fullName || conversation?.displayName || item?.customer || item?.recipient;

  const loans = customer?.loans ?? [];
  const currency = loans[0]?.currency ?? 'MXN';
  const overdue = loans.reduce((sum, l) => sum + l.overdueBalance, 0);
  const total = loans.reduce((sum, l) => sum + l.totalBalance, 0);
  const maxDays = loans.reduce((m, l) => Math.max(m, l.daysPastDue), 0);
  const pendingPromise = customer?.promises.find((p) => promiseStatus(p.status).key === 'Pending');

  return (
    <Drawer anchor="right" open={!!item} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 620 } } }}>
      {item && sessionId && !conversation && !error && <LinearProgress />}
      {item && (
        <Stack sx={{ height: '100%' }}>
          <Box sx={{ p: 3, pb: 2 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Conversación
                </Typography>
                <Typography variant="h5">{name}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {item.recipient}
                  {(conversation?.campaignName ?? item.campaignName) ? ` · ${conversation?.campaignName ?? item.campaignName}` : ''}
                </Typography>
              </Box>
              <Chip label={st.label} color={st.color} variant="soft" />
            </Stack>

            {(conversation?.reason ?? item.reason) && (
              <Alert severity={state === 'pending' ? 'warning' : 'info'} sx={{ mt: 2 }}>
                {conversation?.reason ?? item.reason}
                {item.since ? ` · hace ${waitingFor(item.since)}` : ''}
              </Alert>
            )}

            {customer && (
              <Card variant="outlined" sx={{ mt: 2, px: 2, py: 1.5 }}>
                <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
                  <Box>
                    <Typography variant="caption" color="text.secondary">
                      Vencido
                    </Typography>
                    <Typography variant="subtitle1">{money(overdue, currency)}</Typography>
                  </Box>
                  <Box>
                    <Typography variant="caption" color="text.secondary">
                      Saldo total
                    </Typography>
                    <Typography variant="subtitle1">{money(total, currency)}</Typography>
                  </Box>
                  <Box>
                    <Typography variant="caption" color="text.secondary">
                      Atraso
                    </Typography>
                    <Typography variant="subtitle1">{maxDays} días</Typography>
                  </Box>
                  <Box>
                    <Typography variant="caption" color="text.secondary">
                      Promesa
                    </Typography>
                    <Typography variant="subtitle1">
                      {pendingPromise
                        ? `${day(pendingPromise.promisedDate)}${pendingPromise.amount ? ` · ${money(pendingPromise.amount, currency)}` : ''}`
                        : 'Ninguna'}
                    </Typography>
                  </Box>
                </Stack>
                {item.partyId && (
                  <Button size="small" sx={{ mt: 1, px: 0 }} onClick={() => onOpenCustomer(item.partyId!)}>
                    Ver ficha del cliente ({loans.length} préstamos)
                  </Button>
                )}
              </Card>
            )}

            <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
              {sessionId && state !== 'human' && (
                <Button variant="contained" size="small" disabled={busy} startIcon={<Iconify icon="mdi:account-arrow-left" />} onClick={take}>
                  Tomar conversación
                </Button>
              )}
              {sessionId && (state === 'human' || state === 'pending') && (
                <Button variant="outlined" size="small" disabled={busy} startIcon={<Iconify icon="mdi:robot-outline" />} onClick={release}>
                  Devolver al asistente
                </Button>
              )}
              {!sessionId && item.contactId && (
                <Button variant="outlined" size="small" disabled={busy} startIcon={<Iconify icon="mdi:check" />} onClick={resolveContact}>
                  Marcar como atendido
                </Button>
              )}
            </Stack>
          </Box>

          <Divider />

          <Box sx={{ flex: 1, overflowY: 'auto', px: 3, py: 2, bgcolor: 'background.neutral' }}>
            {!sessionId && (
              <Stack spacing={1}>
                <Typography variant="body2" color="text.secondary">
                  Este contacto no tiene una conversación abierta. Lo que escribas se envía por el canal de la campaña.
                </Typography>
                {item.lastCustomerMessage && <Bubble actor="customer" content={item.lastCustomerMessage} at={item.since} />}
              </Stack>
            )}
            {conversation?.messages.length === 0 && (
              <Typography variant="body2" color="text.secondary">
                Aún no hay mensajes.
              </Typography>
            )}
            <Stack spacing={1}>
              {conversation?.messages.map((m) => (
                <Bubble key={m.id} actor={m.actor} content={m.content} at={m.createdAt} status={m.status} />
              ))}
            </Stack>
            <div ref={bottomRef} />
          </Box>

          <Divider />

          <Box sx={{ p: 2 }}>
            {error && (
              <Alert severity="error" sx={{ mb: 1 }}>
                {error}
              </Alert>
            )}
            {notice && (
              <Alert severity="success" sx={{ mb: 1 }}>
                {notice}
              </Alert>
            )}
            <Stack direction="row" spacing={1} alignItems="flex-end">
              <TextField
                fullWidth
                multiline
                maxRows={5}
                size="small"
                placeholder="Escribe tu respuesta al cliente…"
                value={text}
                onChange={(e) => setText(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    send();
                  }
                }}
              />
              <Button variant="contained" disabled={busy || !text.trim()} onClick={send} endIcon={<Iconify icon="mdi:send" />}>
                Enviar
              </Button>
            </Stack>
            {sessionId && state !== 'human' && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                Al responder tomas la conversación y el asistente deja de contestar.
              </Typography>
            )}
          </Box>
        </Stack>
      )}
    </Drawer>
  );
}

function Bubble({
  actor,
  content,
  at,
  status,
}: {
  actor: ConversationMessage['actor'];
  content: string;
  at?: string | null;
  status?: string | null;
}) {
  const fromCustomer = actor === 'customer';
  return (
    <Stack alignItems={fromCustomer ? 'flex-start' : 'flex-end'}>
      <Box
        sx={{
          maxWidth: '80%',
          px: 1.5,
          py: 1,
          borderRadius: 1.5,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          bgcolor: fromCustomer ? 'background.paper' : actor === 'human' ? 'info.lighter' : 'primary.lighter',
          border: fromCustomer ? 1 : 0,
          borderColor: 'divider',
        }}
      >
        <Typography variant="body2">{content}</Typography>
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ mt: 0.25 }}>
        {ACTOR_LABEL[actor]} · {dateTime(at)}
        {status === 'failed' ? ' · no se entregó' : ''}
      </Typography>
    </Stack>
  );
}
