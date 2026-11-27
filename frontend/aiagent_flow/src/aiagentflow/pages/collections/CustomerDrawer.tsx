import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import {
  money,
  todayIso,
  stageKey,
  PromiseList,
  STAGE_COLOR,
  STAGE_LABEL,
  SectionTitle,
  ContactHistory,
  loanStatusLabel,
} from './shared';

import type { StageKey, CollectionPromise, ContactHistoryItem } from './shared';

// ----------------------------------------------------------------------

type CustomerLoan = {
  id: string;
  loanExternalId: string;
  product?: string;
  currency: string;
  totalBalance: number;
  overdueBalance: number;
  daysPastDue: number;
  stage: StageKey | number;
  status: number;
};

type CustomerDetail = {
  party: { id: string; displayName?: string; fullName?: string; phone?: string; email?: string } | null;
  loans: CustomerLoan[];
  promises: CollectionPromise[];
  contacts: ContactHistoryItem[];
};

type Props = {
  base: string;
  partyId: string | null;
  onClose: () => void;
  onOpenLoan: (loanId: string) => void;
};

export function CustomerDrawer({ base, partyId, onClose, onOpenLoan }: Props) {
  const [detail, setDetail] = useState<CustomerDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [promiseOpen, setPromiseOpen] = useState(false);

  const load = useCallback(() => {
    if (!partyId) return;
    setError(null);
    axios
      .get<CustomerDetail>(`${base}/customers/${partyId}`)
      .then((res) => setDetail(res.data))
      .catch(() => setError('No se pudo cargar el cliente.'));
  }, [base, partyId]);

  useEffect(() => {
    setDetail(null);
    load();
  }, [load]);

  const loans = detail?.loans ?? [];
  const name = detail?.party?.displayName || detail?.party?.fullName || 'Cliente';
  const currency = loans[0]?.currency ?? 'MXN';
  const overdue = loans.reduce((sum, l) => sum + l.overdueBalance, 0);
  const worst = loans.reduce<CustomerLoan | null>((w, l) => (!w || l.daysPastDue > w.daysPastDue ? l : w), null);
  const worstStage = worst ? stageKey(worst.stage) : 'Current';

  return (
    <Drawer anchor="right" open={!!partyId} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 560 } } }}>
      {!detail && !error && <LinearProgress />}
      {error && (
        <Alert severity="error" sx={{ m: 3 }}>
          {error}
        </Alert>
      )}
      {detail && (
        <Box sx={{ p: 3 }}>
          <Typography variant="overline" color="text.secondary">
            Cliente
          </Typography>
          <Typography variant="h5">{name}</Typography>
          <Typography variant="body2" color="text.secondary">
            {detail.party?.phone || 'sin teléfono'}
            {detail.party?.email ? ` · ${detail.party.email}` : ''}
          </Typography>

          <Stack direction="row" spacing={1} sx={{ my: 2 }} flexWrap="wrap" useFlexGap>
            <Chip label={`${loans.length} préstamos`} />
            <Chip label={`${money(overdue, currency)} vencido`} color="warning" variant="soft" />
            <Chip label={STAGE_LABEL[worstStage]} color={STAGE_COLOR[worstStage]} variant="soft" />
          </Stack>

          <Stack direction="row" spacing={1}>
            <Button
              variant="contained"
              size="small"
              startIcon={<Iconify icon="mdi:handshake-outline" />}
              onClick={() => setPromiseOpen(true)}
            >
              Registrar promesa
            </Button>
          </Stack>

          <SectionTitle>Préstamos</SectionTitle>
          <Stack spacing={1}>
            {loans.map((loan) => {
              const st = stageKey(loan.stage);
              return (
                <Card
                  key={loan.id}
                  variant="outlined"
                  sx={{ px: 1.5, py: 1, cursor: 'pointer', '&:hover': { borderColor: 'primary.main' } }}
                  onClick={() => onOpenLoan(loan.id)}
                >
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="subtitle2">
                      {loan.loanExternalId} {loan.product ? `· ${loan.product}` : ''}
                    </Typography>
                    <Chip size="small" label={STAGE_LABEL[st]} color={STAGE_COLOR[st]} variant="soft" />
                  </Stack>
                  <Typography variant="caption" color="text.secondary">
                    {loanStatusLabel(loan.status)} · {loan.daysPastDue} días · vencido {money(loan.overdueBalance, loan.currency)} ·
                    total {money(loan.totalBalance, loan.currency)}
                  </Typography>
                </Card>
              );
            })}
          </Stack>

          <SectionTitle>Promesas de pago</SectionTitle>
          <PromiseList promises={detail.promises} currency={currency} />

          <SectionTitle>Contactos de campañas</SectionTitle>
          <ContactHistory contacts={detail.contacts} />

          <Divider sx={{ my: 3 }} />
          <Button fullWidth variant="outlined" onClick={onClose}>
            Cerrar
          </Button>
        </Box>
      )}

      <PromiseDialog
        base={base}
        partyId={partyId}
        currency={currency}
        suggested={overdue}
        open={promiseOpen}
        onClose={() => setPromiseOpen(false)}
        onSaved={() => {
          setPromiseOpen(false);
          load();
        }}
      />
    </Drawer>
  );
}

function PromiseDialog({
  base,
  partyId,
  currency,
  suggested,
  open,
  onClose,
  onSaved,
}: {
  base: string;
  partyId: string | null;
  currency: string;
  suggested: number;
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(todayIso());
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setAmount(suggested > 0 ? suggested.toFixed(2) : '');
      setDate(todayIso());
      setNotes('');
      setError(null);
    }
  }, [open, suggested]);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      await axios.post(`${base}/promises`, {
        partyId,
        amount: amount ? Number(amount) : null,
        promisedDate: `${date}T00:00:00Z`,
        notes: notes || null,
      });
      onSaved();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo registrar la promesa.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Registrar promesa de pago</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <TextField
            label={`Monto (${currency})`}
            type="number"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            helperText="Déjalo vacío si el cliente no indicó monto."
          />
          <TextField
            label="Fecha prometida"
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            InputLabelProps={{ shrink: true }}
          />
          <TextField label="Notas" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={busy || !date} onClick={save}>
          Guardar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
