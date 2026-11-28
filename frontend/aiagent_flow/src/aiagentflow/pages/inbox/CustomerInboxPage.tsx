import { Helmet } from 'react-helmet-async';
import { useRef, useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Badge from '@mui/material/Badge';
import Stack from '@mui/material/Stack';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import ToggleButton from '@mui/material/ToggleButton';
import LinearProgress from '@mui/material/LinearProgress';
import FormControlLabel from '@mui/material/FormControlLabel';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';

import axios from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { MediaPreview } from '../../components/MediaPreview';
import { initials, fullTime, shortTime, providerOf, ACTOR_LABEL, attachmentsOf } from './inboxModel';

import type { InboxCustomer, InboxChannelRef, InboxTimelineItem } from './inboxModel';

// ----------------------------------------------------------------------

type ChannelOption = { id: string; name: string };

/** A channel chip: which WhatsApp provider (QR, Meta, Twilio), API or web, and the channel's name. */
function ChannelChip({ channel, size = 'small' }: { channel: InboxChannelRef; size?: 'small' | 'medium' }) {
  const p = providerOf(channel.provider);
  return (
    <Tooltip title={`${channel.channelName}${channel.businessAddress ? ` · ${channel.businessAddress}` : ''}`}>
      <Chip
        size={size}
        variant="outlined"
        color={p.color}
        icon={<Iconify icon={p.icon} width={14} />}
        label={p.label}
        sx={{ height: 22, '& .MuiChip-label': { px: 0.75, fontSize: 11 } }}
      />
    </Tooltip>
  );
}

function AttentionChip({ customer }: { customer: InboxCustomer }) {
  if (customer.handoff === 'human') return <Chip size="small" color="info" label="Con asesor" sx={{ height: 20 }} />;
  if (customer.handoff === 'pending') return <Chip size="small" color="warning" label="Espera asesor" sx={{ height: 20 }} />;
  if (customer.replyPending) return <Chip size="small" color="error" variant="outlined" label="Por responder" sx={{ height: 20 }} />;
  return null;
}

// ----------------------------------------------------------------------

export default function CustomerInboxPage() {
  const tenantId = useTenantId();
  const base = `/api/v1/tenants/${tenantId}`;

  const [customers, setCustomers] = useState<InboxCustomer[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [query, setQuery] = useState('');
  const [provider, setProvider] = useState<string>('');
  const [channelId, setChannelId] = useState('');
  const [attention, setAttention] = useState(false);
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [channels, setChannels] = useState<ChannelOption[]>([]);

  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [timeline, setTimeline] = useState<InboxTimelineItem[]>([]);
  const [timelineLoading, setTimelineLoading] = useState(false);
  const [replyChannel, setReplyChannel] = useState('');
  const [text, setText] = useState('');
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  const selected = useMemo(() => customers.find((c) => c.key === selectedKey) ?? null, [customers, selectedKey]);

  const loadCustomers = useCallback(() => {
    setLoading(true);
    setError(null);
    axios
      .get(`${base}/customer-inbox/customers`, {
        params: { query: query || undefined, provider: provider || undefined, channelId: channelId || undefined, attention, includeDeleted, pageSize: 100 },
      })
      .then((res) => {
        setCustomers(res.data?.items ?? []);
        setTotal(res.data?.total ?? 0);
      })
      .catch((err) => setError(err?.response?.data?.message ?? 'No se pudo cargar la bandeja.'))
      .finally(() => setLoading(false));
  }, [base, query, provider, channelId, attention, includeDeleted]);

  const loadTimeline = useCallback(
    (key: string) => {
      setTimelineLoading(true);
      axios
        .get<InboxTimelineItem[]>(`${base}/customer-inbox/customers/${encodeURIComponent(key)}/timeline`, { params: { includeDeleted } })
        .then((res) => setTimeline(res.data ?? []))
        .catch(() => setTimeline([]))
        .finally(() => setTimelineLoading(false));
    },
    [base, includeDeleted]
  );

  useEffect(() => {
    const t = setTimeout(loadCustomers, 250);
    return () => clearTimeout(t);
  }, [loadCustomers]);

  useEffect(() => {
    axios
      .get(`${base}/channels`)
      .then((res) => setChannels((res.data ?? []).map((c: any) => ({ id: String(c.id), name: String(c.name) }))))
      .catch(() => setChannels([]));
  }, [base]);

  useEffect(() => {
    if (!selectedKey) return;
    loadTimeline(selectedKey);
  }, [selectedKey, loadTimeline]);

  useEffect(() => {
    // Reply through the channel the customer last used, unless the advisor picks another.
    if (selected) setReplyChannel(selected.lastChannel?.sessionId ?? selected.channels[0]?.sessionId ?? '');
    setNotice(null);
  }, [selected]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: 'end' });
  }, [timeline]);

  const conversationAction = async (path: string, ok: string) => {
    if (!replyChannel) return;
    try {
      await axios.post(`${base}/conversations/${replyChannel}/${path}`);
      setNotice({ ok: true, text: ok });
      loadCustomers();
    } catch (err: any) {
      setNotice({ ok: false, text: err?.response?.data?.message ?? 'No se pudo completar.' });
    }
  };

  const send = async () => {
    if (!replyChannel || !text.trim()) return;
    setSending(true);
    setNotice(null);
    try {
      await axios.post(`${base}/conversations/${replyChannel}/reply`, { content: text.trim() });
      setText('');
      if (selectedKey) loadTimeline(selectedKey);
      loadCustomers();
    } catch (err: any) {
      setNotice({ ok: false, text: err?.response?.data?.message ?? 'No se pudo enviar el mensaje.' });
    } finally {
      setSending(false);
    }
  };

  const replyRef = selected?.channels.find((c) => c.sessionId === replyChannel) ?? null;

  return (
    <>
      <Helmet>
        <title>Conversaciones</title>
      </Helmet>

      <DashboardContent maxWidth={false}>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} sx={{ mb: 2 }}>
          <Box>
            <Typography variant="h4">Conversaciones</Typography>
            <Typography variant="body2" color="text.secondary">
              Un cliente por fila, sin importar el canal. Cada mensaje dice por dónde entró o salió.
            </Typography>
          </Box>
          <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" />} onClick={loadCustomers}>
            Actualizar
          </Button>
        </Stack>

        <Card sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '360px 1fr', xl: '380px 1fr 300px' }, height: 'calc(100vh - 210px)', minHeight: 520 }}>
          {/* Customers */}
          <Stack sx={{ borderRight: 1, borderColor: 'divider', minHeight: 0 }}>
            <Stack spacing={1} sx={{ p: 1.5 }}>
              <TextField
                size="small"
                placeholder="Buscar por nombre o teléfono"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                InputProps={{ startAdornment: <Iconify icon="mdi:magnify" width={18} sx={{ mr: 1, color: 'text.disabled' }} /> }}
              />
              <ToggleButtonGroup size="small" exclusive value={provider} onChange={(_, v) => setProvider(v ?? '')} sx={{ flexWrap: 'wrap' }}>
                <ToggleButton value="">Todos</ToggleButton>
                <ToggleButton value="qr">QR</ToggleButton>
                <ToggleButton value="meta">Meta</ToggleButton>
                <ToggleButton value="twilio">Twilio</ToggleButton>
                <ToggleButton value="api">API</ToggleButton>
                <ToggleButton value="webchat">Web</ToggleButton>
              </ToggleButtonGroup>
              <TextField select size="small" label="Canal" value={channelId} onChange={(e) => setChannelId(e.target.value)}>
                <MenuItem value="">Todos los canales</MenuItem>
                {channels.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {c.name}
                  </MenuItem>
                ))}
              </TextField>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <FormControlLabel
                  control={<Switch size="small" checked={attention} onChange={(e) => setAttention(e.target.checked)} />}
                  label={<Typography variant="caption">Requieren atención</Typography>}
                />
                <FormControlLabel
                  control={<Switch size="small" checked={includeDeleted} onChange={(e) => setIncludeDeleted(e.target.checked)} />}
                  label={<Typography variant="caption">Canales de prueba</Typography>}
                />
              </Stack>
              <Typography variant="caption" color="text.secondary">
                {total} cliente{total === 1 ? '' : 's'}
              </Typography>
            </Stack>
            {loading && <LinearProgress />}
            {error && <Alert severity="error" sx={{ m: 1.5 }}>{error}</Alert>}
            <Box sx={{ overflowY: 'auto', flex: 1 }}>
              {!loading && customers.length === 0 && (
                <Typography variant="body2" color="text.secondary" sx={{ p: 3, textAlign: 'center' }}>
                  No hay conversaciones con estos filtros.
                </Typography>
              )}
              {customers.map((c) => (
                <Box
                  key={c.key}
                  onClick={() => setSelectedKey(c.key)}
                  sx={{
                    px: 1.5,
                    py: 1.25,
                    cursor: 'pointer',
                    borderBottom: 1,
                    borderColor: 'divider',
                    bgcolor: c.key === selectedKey ? 'action.selected' : 'transparent',
                    '&:hover': { bgcolor: c.key === selectedKey ? 'action.selected' : 'action.hover' },
                  }}
                >
                  <Stack direction="row" spacing={1.25}>
                    <Badge color="error" badgeContent={c.unread} invisible={!c.unread} overlap="circular">
                      <Avatar sx={{ width: 38, height: 38, fontSize: 14, bgcolor: c.registered ? 'primary.main' : 'grey.500' }}>
                        {initials(c.name)}
                      </Avatar>
                    </Badge>
                    <Box sx={{ minWidth: 0, flex: 1 }}>
                      <Stack direction="row" justifyContent="space-between" spacing={1}>
                        <Typography variant="subtitle2" noWrap>
                          {c.name}
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ flexShrink: 0 }}>
                          {shortTime(c.lastActivityAt)}
                        </Typography>
                      </Stack>
                      <Typography variant="caption" color="text.secondary" noWrap component="div">
                        {c.registered ? c.address : 'Sin registrar · ' + c.address}
                      </Typography>
                      <Typography variant="body2" color="text.secondary" noWrap sx={{ mt: 0.25 }}>
                        {c.lastDirection === 'outgoing' ? '↩ ' : ''}
                        {c.lastMessage ?? '—'}
                      </Typography>
                      <Stack direction="row" spacing={0.5} sx={{ mt: 0.75 }} flexWrap="wrap" useFlexGap alignItems="center">
                        {c.channels.map((ch) => (
                          <ChannelChip key={ch.channelId} channel={ch} />
                        ))}
                        <AttentionChip customer={c} />
                      </Stack>
                    </Box>
                  </Stack>
                </Box>
              ))}
            </Box>
          </Stack>

          {/* Timeline */}
          <Stack sx={{ minHeight: 0 }}>
            {!selected ? (
              <Stack alignItems="center" justifyContent="center" sx={{ flex: 1, color: 'text.secondary' }} spacing={1}>
                <Iconify icon="mdi:forum-outline" width={48} />
                <Typography variant="body2">Elige un cliente para ver su conversación en todos sus canales.</Typography>
              </Stack>
            ) : (
              <>
                <Stack direction="row" alignItems="center" spacing={1.5} sx={{ px: 2, py: 1.25, borderBottom: 1, borderColor: 'divider' }}>
                  <Avatar sx={{ width: 36, height: 36, fontSize: 13, bgcolor: selected.registered ? 'primary.main' : 'grey.500' }}>
                    {initials(selected.name)}
                  </Avatar>
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography variant="subtitle1" noWrap>
                      {selected.name}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {selected.address} · {selected.channels.length} canal{selected.channels.length === 1 ? '' : 'es'}
                    </Typography>
                  </Box>
                  <AttentionChip customer={selected} />
                  {selected.handoff ? (
                    <Button size="small" variant="outlined" onClick={() => conversationAction('release', 'La conversación volvió al asistente.')}>
                      Devolver al asistente
                    </Button>
                  ) : (
                    <Button size="small" variant="outlined" onClick={() => conversationAction('take', 'Tomaste la conversación: el asistente queda en pausa.')}>
                      Tomar conversación
                    </Button>
                  )}
                  <IconButton size="small" onClick={() => selectedKey && loadTimeline(selectedKey)}>
                    <Iconify icon="mdi:refresh" />
                  </IconButton>
                </Stack>
                {timelineLoading && <LinearProgress />}
                <Box sx={{ flex: 1, overflowY: 'auto', px: 2, py: 2, bgcolor: 'background.neutral' }}>
                  {timeline.length === 0 && !timelineLoading && (
                    <Typography variant="body2" color="text.secondary" align="center">
                      Sin mensajes.
                    </Typography>
                  )}
                  <Stack spacing={1.25}>
                    {timeline.map((m, i) => {
                      const incoming = m.direction === 'in';
                      const p = providerOf(m.provider);
                      const changedChannel = i > 0 && timeline[i - 1].channelId !== m.channelId;
                      const files = attachmentsOf(m.attachments);
                      const failed = m.status === 'Failed' || m.delivery === 'blocked';
                      return (
                        <Box key={m.id}>
                          {changedChannel && (
                            <Divider sx={{ my: 1 }}>
                              <Chip size="small" icon={<Iconify icon={p.icon} width={14} />} label={`Ahora por ${m.channelName}`} />
                            </Divider>
                          )}
                          <Stack alignItems={incoming ? 'flex-start' : 'flex-end'}>
                            <Box
                              sx={{
                                maxWidth: '75%',
                                px: 1.5,
                                py: 1,
                                borderRadius: 1.5,
                                bgcolor: incoming ? 'background.paper' : m.actor === 'human' ? 'info.lighter' : m.actor === 'campaign' ? 'warning.lighter' : 'primary.lighter',
                                border: failed ? 1 : 0,
                                borderColor: 'error.main',
                                boxShadow: 1,
                              }}
                            >
                              <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                                {m.content}
                              </Typography>
                              {files.map((f, j) =>
                                f.mediaId ? (
                                  <Box key={j} sx={{ mt: 0.75 }}>
                                    <MediaPreview tenantId={tenantId} mediaId={f.mediaId} kind={f.kind} fileName={f.fileName} size={180} />
                                  </Box>
                                ) : (
                                <Chip
                                  key={j}
                                  size="small"
                                  icon={<Iconify icon="mdi:paperclip" width={14} />}
                                  label={f.fileName || f.kind}
                                  component={f.url ? 'a' : 'div'}
                                  href={f.url ?? undefined}
                                  target="_blank"
                                  clickable={!!f.url}
                                  sx={{ mt: 0.5, mr: 0.5 }}
                                />
                                )
                              )}
                              {failed && (
                                <Typography variant="caption" color="error" component="div" sx={{ mt: 0.5 }}>
                                  No se entregó{m.error ? `: ${m.error}` : ''}
                                </Typography>
                              )}
                            </Box>
                            <Typography variant="caption" color="text.secondary" sx={{ mt: 0.25, px: 0.5 }}>
                              {ACTOR_LABEL[m.actor] ?? m.actor} · {p.label} · {m.channelName} · {fullTime(m.createdAt)}
                            </Typography>
                          </Stack>
                        </Box>
                      );
                    })}
                    <div ref={bottomRef} />
                  </Stack>
                </Box>
                <Stack spacing={1} sx={{ p: 1.5, borderTop: 1, borderColor: 'divider' }}>
                  {notice && (
                    <Alert severity={notice.ok ? 'success' : 'warning'} onClose={() => setNotice(null)}>
                      {notice.text}
                    </Alert>
                  )}
                  <Stack direction="row" spacing={1} alignItems="flex-start">
                    <TextField
                      select
                      size="small"
                      label="Responder por"
                      value={replyChannel}
                      onChange={(e) => setReplyChannel(e.target.value)}
                      sx={{ width: 220, flexShrink: 0 }}
                    >
                      {selected.channels.map((c) => (
                        <MenuItem key={c.sessionId} value={c.sessionId} disabled={c.channelDeleted}>
                          {providerOf(c.provider).label} · {c.channelName}
                        </MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      size="small"
                      fullWidth
                      multiline
                      maxRows={4}
                      placeholder={replyRef ? `Escribe como asesor por ${replyRef.channelName}…` : 'Escribe como asesor…'}
                      value={text}
                      onChange={(e) => setText(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' && !e.shiftKey) {
                          e.preventDefault();
                          send();
                        }
                      }}
                    />
                    <Button variant="contained" disabled={sending || !text.trim() || !replyChannel} onClick={send}>
                      Enviar
                    </Button>
                  </Stack>
                  <Typography variant="caption" color="text.secondary">
                    Al responder tomas la conversación y el asistente queda en pausa hasta que la devuelvas.
                  </Typography>
                </Stack>
              </>
            )}
          </Stack>

          {/* Customer */}
          <Box sx={{ borderLeft: 1, borderColor: 'divider', p: 2, display: { xs: 'none', xl: 'block' }, overflowY: 'auto' }}>
            {selected ? (
              <Stack spacing={2}>
                <Stack alignItems="center" spacing={1}>
                  <Avatar sx={{ width: 64, height: 64, fontSize: 22, bgcolor: selected.registered ? 'primary.main' : 'grey.500' }}>
                    {initials(selected.name)}
                  </Avatar>
                  <Typography variant="h6" align="center">
                    {selected.name}
                  </Typography>
                  <Chip
                    size="small"
                    color={selected.registered ? 'success' : 'default'}
                    label={selected.registered ? 'Cliente registrado' : 'Sin registrar'}
                  />
                </Stack>
                <Box>
                  <Typography variant="overline" color="text.secondary">
                    Contacto
                  </Typography>
                  <Typography variant="body2">{selected.address}</Typography>
                </Box>
                <Box>
                  <Typography variant="overline" color="text.secondary">
                    Canales
                  </Typography>
                  <Stack spacing={1} sx={{ mt: 0.5 }}>
                    {selected.channels.map((c) => (
                      <Stack key={c.sessionId} spacing={0.25}>
                        <ChannelChip channel={c} />
                        <Typography variant="caption" color="text.secondary">
                          {c.channelName}
                          {c.businessAddress ? ` · ${c.businessAddress}` : ''} · {shortTime(c.lastActivityAt)}
                        </Typography>
                      </Stack>
                    ))}
                  </Stack>
                </Box>
              </Stack>
            ) : (
              <Typography variant="body2" color="text.secondary">
                Los datos del cliente aparecen aquí.
              </Typography>
            )}
          </Box>
        </Card>
      </DashboardContent>
    </>
  );
}
