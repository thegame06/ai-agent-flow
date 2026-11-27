import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';

import {
  day,
  Field,
  money,
  stageKey,
  Sparkline,
  PromiseList,
  STAGE_COLOR,
  STAGE_LABEL,
  SectionTitle,
  ContactHistory,
  loanStatusLabel,
} from './shared';

import type { StageKey, CollectionPromise, ContactHistoryItem } from './shared';

// ----------------------------------------------------------------------

type Installment = { number: number; dueDate: string; amount: number; paidAmount?: number; status?: string };

type LoanDocument = {
  id: string;
  loanExternalId: string;
  partyId: string;
  customerExternalId?: string;
  customerName?: string;
  customerPhone?: string;
  product?: string;
  currency: string;
  originalAmount?: number;
  disbursedAt?: string;
  paymentFrequency?: string;
  totalBalance: number;
  overduePrincipal: number;
  overdueInterest: number;
  overduePenalties: number;
  overdueFees: number;
  overdueBalance: number;
  daysPastDue: number;
  stage: StageKey | number;
  status: number;
  nextPaymentDate?: string;
  nextPaymentAmount?: number;
  lastPaymentDate?: string;
  lastPaymentAmount?: number;
  installments?: Installment[];
  snapshotDate: string;
  createdAt: string;
};

type Snapshot = { id: string; snapshotDate: string; daysPastDue: number; totalBalance: number; overdueBalance: number };

type OtherLoan = {
  id: string;
  loanExternalId: string;
  product?: string;
  totalBalance: number;
  overdueBalance: number;
  daysPastDue: number;
  stage: StageKey;
  status: string;
  currency: string;
};

type LoanDetail = {
  loan: LoanDocument;
  snapshots: Snapshot[];
  customer: { id: string; displayName?: string; fullName?: string; phone?: string; email?: string } | null;
  otherLoans: OtherLoan[];
  promises: CollectionPromise[];
  contacts: ContactHistoryItem[];
};

type Props = {
  base: string;
  loanId: string | null;
  onClose: () => void;
  onOpenCustomer?: (partyId: string) => void;
};

export function LoanDrawer({ base, loanId, onClose, onOpenCustomer }: Props) {
  const [currentId, setCurrentId] = useState<string | null>(loanId);
  const [detail, setDetail] = useState<LoanDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => setCurrentId(loanId), [loanId]);

  useEffect(() => {
    if (!currentId) return;
    setDetail(null);
    setError(null);
    axios
      .get<LoanDetail>(`${base}/loans/${currentId}`)
      .then((res) => setDetail(res.data))
      .catch(() => setError('No se pudo cargar el préstamo.'));
  }, [base, currentId]);

  const loan = detail?.loan;
  const history = detail ? [...detail.snapshots].reverse() : [];
  const stage = loan ? stageKey(loan.stage) : 'Current';
  const paidRatio =
    loan?.originalAmount && loan.originalAmount > 0
      ? Math.max(0, Math.min(100, ((loan.originalAmount - loan.totalBalance) / loan.originalAmount) * 100))
      : null;

  return (
    <Drawer anchor="right" open={!!loanId} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 600 } } }}>
      {!detail && !error && <LinearProgress />}
      {error && (
        <Alert severity="error" sx={{ m: 3 }}>
          {error}
        </Alert>
      )}
      {loan && detail && (
        <Box sx={{ p: 3 }}>
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
            <Box>
              <Typography variant="overline" color="text.secondary">
                Préstamo
              </Typography>
              <Typography variant="h5">{loan.loanExternalId}</Typography>
              <Typography variant="body2" color="text.secondary">
                {loan.product || 'Sin producto'} · {loanStatusLabel(loan.status)} · corte {day(loan.snapshotDate)}
              </Typography>
            </Box>
            <Chip label={STAGE_LABEL[stage]} color={STAGE_COLOR[stage]} variant="soft" />
          </Stack>

          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1.5, my: 2 }}>
            <Card variant="outlined" sx={{ p: 1.5 }}>
              <Field label="Saldo vencido" value={money(loan.overdueBalance, loan.currency)} />
            </Card>
            <Card variant="outlined" sx={{ p: 1.5 }}>
              <Field label="Días de atraso" value={String(loan.daysPastDue)} />
            </Card>
            <Card variant="outlined" sx={{ p: 1.5 }}>
              <Field label="Saldo total" value={money(loan.totalBalance, loan.currency)} />
            </Card>
          </Box>

          {paidRatio != null && (
            <Box sx={{ mb: 1 }}>
              <Typography variant="caption" color="text.secondary">
                Pagado {paidRatio.toFixed(0)}% de {money(loan.originalAmount, loan.currency)}
              </Typography>
              <LinearProgress variant="determinate" value={paidRatio} sx={{ height: 6, borderRadius: 1 }} />
            </Box>
          )}

          <SectionTitle>Cliente</SectionTitle>
          <Card variant="outlined" sx={{ p: 1.5 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Box>
                <Typography variant="subtitle2">{loan.customerName || 'Sin nombre'}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {loan.customerExternalId} · {loan.customerPhone || 'sin teléfono'}
                  {detail.customer?.email ? ` · ${detail.customer.email}` : ''}
                </Typography>
              </Box>
              {onOpenCustomer && (
                <Button size="small" onClick={() => onOpenCustomer(loan.partyId)}>
                  Ver cliente
                </Button>
              )}
            </Stack>
          </Card>

          <SectionTitle>Desglose del vencido</SectionTitle>
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1 }}>
            <Field label="Capital vencido" value={money(loan.overduePrincipal, loan.currency)} />
            <Field label="Interés vencido" value={money(loan.overdueInterest, loan.currency)} />
            <Field label="Moratorios" value={money(loan.overduePenalties, loan.currency)} />
            <Field label="Comisiones" value={money(loan.overdueFees, loan.currency)} />
          </Box>

          <SectionTitle>Condiciones y pagos</SectionTitle>
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1 }}>
            <Field label="Monto original" value={money(loan.originalAmount, loan.currency)} />
            <Field label="Desembolso" value={day(loan.disbursedAt)} />
            <Field label="Frecuencia de pago" value={loan.paymentFrequency || '—'} />
            <Field label="Moneda" value={loan.currency} />
            <Field label="Próximo pago" value={`${day(loan.nextPaymentDate)} · ${money(loan.nextPaymentAmount, loan.currency)}`} />
            <Field label="Último pago" value={`${day(loan.lastPaymentDate)} · ${money(loan.lastPaymentAmount, loan.currency)}`} />
          </Box>

          {loan.installments && loan.installments.length > 0 && (
            <>
              <SectionTitle>Calendario de pagos</SectionTitle>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>#</TableCell>
                    <TableCell>Vence</TableCell>
                    <TableCell align="right">Monto</TableCell>
                    <TableCell align="right">Pagado</TableCell>
                    <TableCell>Estado</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {loan.installments.map((i) => (
                    <TableRow key={i.number}>
                      <TableCell>{i.number}</TableCell>
                      <TableCell>{day(i.dueDate)}</TableCell>
                      <TableCell align="right">{money(i.amount, loan.currency)}</TableCell>
                      <TableCell align="right">{money(i.paidAmount ?? 0, loan.currency)}</TableCell>
                      <TableCell>{i.status || '—'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </>
          )}

          <SectionTitle>Evolución por corte</SectionTitle>
          {history.length === 0 ? (
            <Typography variant="body2" color="text.secondary">
              Sin cortes registrados.
            </Typography>
          ) : (
            <>
              <Sparkline values={history.map((s) => s.overdueBalance)} />
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Corte</TableCell>
                    <TableCell align="right">Días</TableCell>
                    <TableCell align="right">Vencido</TableCell>
                    <TableCell align="right">Total</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {detail.snapshots.map((s) => (
                    <TableRow key={s.id}>
                      <TableCell>{day(s.snapshotDate)}</TableCell>
                      <TableCell align="right">{s.daysPastDue}</TableCell>
                      <TableCell align="right">{money(s.overdueBalance, loan.currency)}</TableCell>
                      <TableCell align="right">{money(s.totalBalance, loan.currency)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </>
          )}

          <SectionTitle>Promesas de pago</SectionTitle>
          <PromiseList promises={detail.promises} currency={loan.currency} />

          <SectionTitle>Contactos de campañas</SectionTitle>
          <ContactHistory contacts={detail.contacts} />

          {detail.otherLoans.length > 0 && (
            <>
              <SectionTitle>Otros préstamos del cliente</SectionTitle>
              <Stack spacing={1}>
                {detail.otherLoans.map((o) => (
                  <Card
                    key={o.id}
                    variant="outlined"
                    sx={{ px: 1.5, py: 1, cursor: 'pointer', '&:hover': { borderColor: 'primary.main' } }}
                    onClick={() => setCurrentId(o.id)}
                  >
                    <Stack direction="row" justifyContent="space-between" alignItems="center">
                      <Typography variant="subtitle2">
                        {o.loanExternalId} {o.product ? `· ${o.product}` : ''}
                      </Typography>
                      <Chip size="small" label={STAGE_LABEL[o.stage]} color={STAGE_COLOR[o.stage]} variant="soft" />
                    </Stack>
                    <Typography variant="caption" color="text.secondary">
                      {loanStatusLabel(o.status)} · {o.daysPastDue} días · vencido {money(o.overdueBalance, o.currency)} · total{' '}
                      {money(o.totalBalance, o.currency)}
                    </Typography>
                  </Card>
                ))}
              </Stack>
            </>
          )}

          <Divider sx={{ my: 3 }} />
          <Button fullWidth variant="outlined" onClick={onClose}>
            Cerrar
          </Button>
        </Box>
      )}
    </Drawer>
  );
}
