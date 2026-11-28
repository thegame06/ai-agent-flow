import { useState, useEffect, useCallback } from 'react';

import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';

import { day, money, todayIso, dateTime } from './shared';
import { MediaPreview } from '../../components/MediaPreview';

// ----------------------------------------------------------------------

export type PaymentReport = {
  id: string;
  partyId: string;
  customerName?: string | null;
  phone?: string | null;
  amount?: number | null;
  currency: string;
  paidOnText?: string | null;
  paidOn?: string | null;
  reference?: string | null;
  /** Photos or PDFs of the receipt the customer sent. */
  receiptMediaIds?: string[] | null;
  method?: string | null;
  notes?: string | null;
  loanId?: string | null;
  status: 'Pending' | 'Confirmed' | 'Rejected' | number;
  reviewNotes?: string | null;
  createdAt: string;
};

const STATUS: Record<string, { label: string; color: 'warning' | 'success' | 'default' }> = {
  Pending: { label: 'Por verificar', color: 'warning' },
  Confirmed: { label: 'Confirmado', color: 'success' },
  Rejected: { label: 'Rechazado', color: 'default' },
};
const statusKey = (s: PaymentReport['status']) => (typeof s === 'number' ? ['Pending', 'Confirmed', 'Rejected'][s] : s);

type CustomerLoan = { id: string; loanExternalId: string; overdueBalance: number; currency: string; status: number | string };

type Props = { base: string; reloadKey: number; onOpenCustomer: (partyId: string) => void; onCount?: (pending: number) => void };

/** Payments customers told the assistant they made. A person confirms them (registers the payment) or rejects them. */
const tenantOf = (base: string) => base.match(/tenants\/([^/]+)/)?.[1] ?? '';

export function PaymentReportsTab({ base, reloadKey, onOpenCustomer, onCount }: Props) {
  const [status, setStatus] = useState('Pending');
  const [items, setItems] = useState<PaymentReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [confirming, setConfirming] = useState<PaymentReport | null>(null);
  const [rejecting, setRejecting] = useState<PaymentReport | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    axios
      .get<{ items: PaymentReport[]; pending: number }>(`${base}/payment-reports`, { params: { status: status || undefined } })
      .then((res) => {
        setItems(res.data.items ?? []);
        onCount?.(res.data.pending ?? 0);
      })
      .finally(() => setLoading(false));
  }, [base, status, onCount]);

  useEffect(() => {
    load();
  }, [load, reloadKey]);

  return (
    <>
      <Stack direction="row" spacing={2} sx={{ p: 2 }} alignItems="center">
        <TextField select size="small" label="Estado" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ minWidth: 180 }}>
          <MenuItem value="">Todos</MenuItem>
          {Object.entries(STATUS).map(([key, s]) => (
            <MenuItem key={key} value={key}>
              {s.label}
            </MenuItem>
          ))}
        </TextField>
        <Typography variant="body2" color="text.secondary">
          Pagos que los clientes dijeron haber hecho. Al confirmarlo se registra el pago en el préstamo y se cierra la promesa
          que cubre.
        </Typography>
      </Stack>
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Cliente</TableCell>
              <TableCell align="right">Monto</TableCell>
              <TableCell>Cuándo pagó</TableCell>
              <TableCell>Referencia</TableCell>
              <TableCell>Estado</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={6} align="center" sx={{ py: 4 }}>
                  <CircularProgress size={24} />
                </TableCell>
              </TableRow>
            )}
            {!loading && items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} align="center" sx={{ py: 4, color: 'text.secondary' }}>
                  No hay pagos reportados en este estado. Aparecen cuando un cliente le dice al asistente que ya pagó.
                </TableCell>
              </TableRow>
            )}
            {!loading &&
              items.map((r) => {
                const st = STATUS[statusKey(r.status)] ?? STATUS.Pending;
                return (
                  <TableRow key={r.id} hover sx={{ cursor: 'pointer' }} onClick={() => onOpenCustomer(r.partyId)}>
                    <TableCell>
                      <Typography variant="subtitle2">{r.customerName || 'Cliente'}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        reportado {dateTime(r.createdAt)}
                      </Typography>
                    </TableCell>
                    <TableCell align="right">{r.amount ? money(r.amount, r.currency) : 'No indicado'}</TableCell>
                    <TableCell>
                      {r.paidOn ? day(r.paidOn) : '—'}
                      {r.paidOnText && (
                        <Typography variant="caption" color="text.secondary" display="block">
                          “{r.paidOnText}”
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell onClick={(e) => (r.receiptMediaIds?.length ? e.stopPropagation() : undefined)}>
                      {r.reference || '—'}
                      {r.method && (
                        <Typography variant="caption" color="text.secondary" display="block">
                          {r.method}
                        </Typography>
                      )}
                      {!!r.receiptMediaIds?.length && (
                        <Stack direction="row" spacing={0.5} sx={{ mt: 0.75 }}>
                          {r.receiptMediaIds.map((id) => (
                            <MediaPreview key={id} tenantId={tenantOf(base)} mediaId={id} kind="image" size={56} />
                          ))}
                        </Stack>
                      )}
                    </TableCell>
                    <TableCell>
                      <Chip size="small" label={st.label} color={st.color} variant="soft" />
                      {r.reviewNotes && (
                        <Typography variant="caption" color="text.secondary" display="block">
                          {r.reviewNotes}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                      {statusKey(r.status) === 'Pending' && (
                        <Stack direction="row" spacing={1} justifyContent="flex-end">
                          <Button size="small" variant="contained" onClick={() => setConfirming(r)}>
                            Confirmar
                          </Button>
                          <Button size="small" color="inherit" onClick={() => setRejecting(r)}>
                            Rechazar
                          </Button>
                        </Stack>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
          </TableBody>
        </Table>
      </TableContainer>

      <ConfirmDialog
        base={base}
        report={confirming}
        onClose={() => setConfirming(null)}
        onDone={() => {
          setConfirming(null);
          load();
        }}
      />
      <RejectDialog
        base={base}
        report={rejecting}
        onClose={() => setRejecting(null)}
        onDone={() => {
          setRejecting(null);
          load();
        }}
      />
    </>
  );
}

function ConfirmDialog({
  base,
  report,
  onClose,
  onDone,
}: {
  base: string;
  report: PaymentReport | null;
  onClose: () => void;
  onDone: () => void;
}) {
  const [loans, setLoans] = useState<CustomerLoan[]>([]);
  const [loanId, setLoanId] = useState('');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(todayIso());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!report) return;
    setError(null);
    setAmount(report.amount ? String(report.amount) : '');
    setDate(report.paidOn ? report.paidOn.slice(0, 10) : todayIso());
    setLoanId(report.loanId ?? '');
    axios
      .get<{ loans: CustomerLoan[] }>(`${base}/customers/${report.partyId}`)
      .then((res) => {
        const open = (res.data.loans ?? []).filter((l) => l.status !== 1 && l.status !== 'PaidOff');
        setLoans(open);
        if (!report.loanId && open.length) setLoanId([...open].sort((a, b) => b.overdueBalance - a.overdueBalance)[0].id);
      })
      .catch(() => setLoans([]));
  }, [base, report]);

  const confirm = async () => {
    if (!report) return;
    setBusy(true);
    setError(null);
    try {
      await axios.post(`${base}/payment-reports/${report.id}/confirm`, {
        loanId: loanId || null,
        amount: amount ? Number(amount) : null,
        paidAt: `${date}T12:00:00Z`,
      });
      onDone();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo confirmar.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!report} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Confirmar pago recibido</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {!!report?.receiptMediaIds?.length && (
            <Stack spacing={1}>
              <Typography variant="subtitle2">Comprobante que envió el cliente</Typography>
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                {report.receiptMediaIds.map((id) => (
                  <MediaPreview key={id} tenantId={tenantOf(base)} mediaId={id} kind="image" size={240} />
                ))}
              </Stack>
            </Stack>
          )}
          <Typography variant="body2" color="text.secondary">
            Verifica que el dinero llegó. Se registrará el pago en el préstamo
            {report?.reference ? ` con la referencia ${report.reference}` : ''}.
          </Typography>
          <TextField select label="Préstamo" value={loanId} onChange={(e) => setLoanId(e.target.value)}>
            {loans.map((l) => (
              <MenuItem key={l.id} value={l.id}>
                {l.loanExternalId} · vencido {money(l.overdueBalance, l.currency)}
              </MenuItem>
            ))}
          </TextField>
          <TextField label={`Monto recibido (${report?.currency ?? ''})`} type="number" value={amount} onChange={(e) => setAmount(e.target.value)} />
          <TextField label="Fecha del pago" type="date" value={date} onChange={(e) => setDate(e.target.value)} InputLabelProps={{ shrink: true }} />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={busy || !amount || !loanId} onClick={confirm}>
          Registrar pago
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function RejectDialog({
  base,
  report,
  onClose,
  onDone,
}: {
  base: string;
  report: PaymentReport | null;
  onClose: () => void;
  onDone: () => void;
}) {
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (report) setNotes('');
  }, [report]);

  const reject = async () => {
    if (!report) return;
    setBusy(true);
    try {
      await axios.post(`${base}/payment-reports/${report.id}/reject`, { notes: notes || null });
      onDone();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!report} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Rechazar pago reportado</DialogTitle>
      <DialogContent>
        <TextField
          sx={{ mt: 1 }}
          fullWidth
          label="Motivo"
          placeholder="No se encontró el depósito"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          multiline
          minRows={2}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" color="error" disabled={busy} onClick={reject}>
          Rechazar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
