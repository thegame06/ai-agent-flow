import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import TableContainer from '@mui/material/TableContainer';
import TablePagination from '@mui/material/TablePagination';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

type StageKey = 'Current' | 'Dpd1To30' | 'Dpd31To60' | 'Dpd61To90' | 'Dpd90Plus';

type Summary = {
  customers: number;
  loans: number;
  activeLoans: number;
  totalBalance: number;
  overdueBalance: number;
  currencies: string[];
  byStage: { stage: StageKey; label: string; loans: number; customers: number; overdueBalance: number }[];
  lastImport: { id: string; snapshotDate: string; createdAt: string; source: string; fileName?: string } | null;
};

type CustomerRow = {
  partyId: string;
  customerExternalId?: string;
  name?: string;
  phone?: string;
  loans: number;
  activeLoans: number;
  totalBalance: number;
  overdueBalance: number;
  maxDaysPastDue: number;
  stage: StageKey;
  stageLabel: string;
  currency: string;
};

type Loan = {
  id: string;
  loanExternalId: string;
  product?: string;
  currency: string;
  originalAmount?: number;
  paymentFrequency?: string;
  totalBalance: number;
  overduePrincipal: number;
  overdueInterest: number;
  overduePenalties: number;
  overdueFees: number;
  overdueBalance: number;
  daysPastDue: number;
  // The loan document serializes the enum as its numeric value.
  stage: StageKey | number;
  status: number;
  nextPaymentDate?: string;
  nextPaymentAmount?: number;
  lastPaymentDate?: string;
  lastPaymentAmount?: number;
  snapshotDate: string;
};

type Snapshot = { id: string; snapshotDate: string; daysPastDue: number; totalBalance: number; overdueBalance: number };

type ImportResult = {
  dryRun: boolean;
  batchId?: string;
  snapshotDate: string;
  totalRows: number;
  validRows: number;
  customers: number;
  loansCreated: number;
  loansUpdated: number;
  failed: number;
  errors: { row: number; field: string; message: string }[];
};

const STAGE_COLOR: Record<StageKey, 'success' | 'info' | 'warning' | 'error' | 'default'> = {
  Current: 'success',
  Dpd1To30: 'info',
  Dpd31To60: 'warning',
  Dpd61To90: 'error',
  Dpd90Plus: 'error',
};

const STAGE_LABEL: Record<StageKey, string> = {
  Current: 'Al corriente',
  Dpd1To30: '1-30 días',
  Dpd31To60: '31-60 días',
  Dpd61To90: '61-90 días',
  Dpd90Plus: '90+ días',
};

const LOAN_STATUS = ['Activo', 'Liquidado', 'Castigado', 'Reestructurado'];

const STAGE_BY_INDEX: StageKey[] = ['Current', 'Dpd1To30', 'Dpd31To60', 'Dpd61To90', 'Dpd90Plus'];

const stageKey = (stage: StageKey | number): StageKey =>
  typeof stage === 'number' ? (STAGE_BY_INDEX[stage] ?? 'Current') : stage;

const money = (value: number | undefined | null, currency = 'MXN') =>
  value == null
    ? '—'
    : new Intl.NumberFormat('es-MX', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value);

const day = (value?: string) => (value ? new Date(value).toLocaleDateString('es-MX', { timeZone: 'UTC' }) : '—');

const todayIso = () => new Date().toISOString().slice(0, 10);

// ----------------------------------------------------------------------

export default function CollectionsPage() {
  const tenantId = useTenantId();
  const base = `/api/v1/tenants/${tenantId}/collections`;

  const [summary, setSummary] = useState<Summary | null>(null);
  const [customers, setCustomers] = useState<CustomerRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState('');
  const [stage, setStage] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [selected, setSelected] = useState<CustomerRow | null>(null);
  const [importOpen, setImportOpen] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [summaryRes, customersRes] = await Promise.all([
        axios.get<Summary>(`${base}/summary`),
        axios.get(`${base}/customers`, { params: { query: query || undefined, stage: stage || undefined, page, pageSize: 25 } }),
      ]);
      setSummary(summaryRes.data);
      setCustomers(customersRes.data.items ?? []);
      setTotal(customersRes.data.total ?? 0);
    } catch (err: any) {
      const code = err?.response?.data?.errorCode;
      setError(
        code === 'APP_MODULE_DISABLED'
          ? 'La app de Cobranza no está habilitada para este cliente. Actívala en Integraciones.'
          : 'No se pudo cargar la cartera.'
      );
    } finally {
      setLoading(false);
    }
  }, [base, query, stage, page]);

  useEffect(() => {
    load();
  }, [load]);

  const downloadTemplate = async () => {
    const res = await axios.get(`${base}/imports/template`, { responseType: 'blob' });
    const url = URL.createObjectURL(res.data);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'plantilla-cartera.csv';
    link.click();
    URL.revokeObjectURL(url);
  };

  const currency = summary?.currencies?.length === 1 ? summary.currencies[0] : 'MXN';

  return (
    <>
      <Helmet>
        <title>Cartera de cobranza</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} sx={{ mb: 3 }}>
          <Box>
            <Typography variant="h4">Cartera de cobranza</Typography>
            <Typography variant="body2" color="text.secondary">
              Clientes y sus préstamos.{' '}
              {summary?.lastImport
                ? `Corte del ${day(summary.lastImport.snapshotDate)} · última carga ${new Date(summary.lastImport.createdAt).toLocaleString('es-MX')}`
                : 'Aún no hay cargas.'}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center">
            <Button variant="outlined" startIcon={<Iconify icon="mdi:file-download-outline" />} onClick={downloadTemplate}>
              Plantilla CSV
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mdi:upload" />} onClick={() => setImportOpen(true)}>
              Importar cartera
            </Button>
          </Stack>
        </Stack>

        {error && (
          <Alert severity="warning" sx={{ mb: 3 }}>
            {error}
          </Alert>
        )}

        {summary && (
          <>
            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, mb: 2 }}>
              <Kpi label="Clientes" value={summary.customers.toLocaleString('es-MX')} />
              <Kpi label="Préstamos activos" value={summary.activeLoans.toLocaleString('es-MX')} />
              <Kpi label="Saldo total" value={money(summary.totalBalance, currency)} />
              <Kpi label="Saldo vencido" value={money(summary.overdueBalance, currency)} />
            </Box>

            <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(5, 1fr)' }, mb: 3 }}>
              {summary.byStage.map((s) => (
                <Card
                  key={s.stage}
                  variant="outlined"
                  onClick={() => {
                    setPage(0);
                    setStage(stage === s.stage ? '' : s.stage);
                  }}
                  sx={{
                    cursor: 'pointer',
                    borderColor: stage === s.stage ? 'primary.main' : undefined,
                    borderWidth: stage === s.stage ? 2 : 1,
                  }}
                >
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Chip size="small" label={s.label} color={STAGE_COLOR[s.stage]} variant="soft" sx={{ mb: 1 }} />
                    <Typography variant="h6">{s.loans.toLocaleString('es-MX')} préstamos</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {s.customers} clientes · {money(s.overdueBalance, currency)} vencido
                    </Typography>
                  </CardContent>
                </Card>
              ))}
            </Box>
          </>
        )}

        <Card>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2 }}>
            <TextField
              size="small"
              placeholder="Buscar por nombre, teléfono o id de cliente"
              value={query}
              onChange={(e) => {
                setPage(0);
                setQuery(e.target.value);
              }}
              sx={{ flex: 1 }}
            />
            <TextField
              select
              size="small"
              label="Etapa"
              value={stage}
              onChange={(e) => {
                setPage(0);
                setStage(e.target.value);
              }}
              sx={{ minWidth: 180 }}
            >
              <MenuItem value="">Todas</MenuItem>
              {(Object.keys(STAGE_LABEL) as StageKey[]).map((key) => (
                <MenuItem key={key} value={key}>
                  {STAGE_LABEL[key]}
                </MenuItem>
              ))}
            </TextField>
          </Stack>

          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Cliente</TableCell>
                  <TableCell>Teléfono</TableCell>
                  <TableCell align="right">Préstamos</TableCell>
                  <TableCell align="right">Saldo vencido</TableCell>
                  <TableCell align="right">Saldo total</TableCell>
                  <TableCell align="right">Días de atraso</TableCell>
                  <TableCell>Etapa</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {loading && (
                  <TableRow>
                    <TableCell colSpan={7} align="center" sx={{ py: 4 }}>
                      <CircularProgress size={24} />
                    </TableCell>
                  </TableRow>
                )}
                {!loading && customers.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={7} align="center" sx={{ py: 4, color: 'text.secondary' }}>
                      No hay clientes. Importa tu cartera con la plantilla CSV.
                    </TableCell>
                  </TableRow>
                )}
                {!loading &&
                  customers.map((c) => (
                    <TableRow key={c.partyId} hover sx={{ cursor: 'pointer' }} onClick={() => setSelected(c)}>
                      <TableCell>
                        <Typography variant="subtitle2">{c.name || 'Sin nombre'}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {c.customerExternalId}
                        </Typography>
                      </TableCell>
                      <TableCell>{c.phone || '—'}</TableCell>
                      <TableCell align="right">
                        {c.loans}
                        {c.activeLoans !== c.loans ? ` (${c.activeLoans} activos)` : ''}
                      </TableCell>
                      <TableCell align="right">{money(c.overdueBalance, c.currency)}</TableCell>
                      <TableCell align="right">{money(c.totalBalance, c.currency)}</TableCell>
                      <TableCell align="right">{c.maxDaysPastDue}</TableCell>
                      <TableCell>
                        <Chip size="small" label={c.stageLabel} color={STAGE_COLOR[c.stage]} variant="soft" />
                      </TableCell>
                    </TableRow>
                  ))}
              </TableBody>
            </Table>
          </TableContainer>
          <TablePagination
            component="div"
            count={total}
            page={page}
            rowsPerPage={25}
            rowsPerPageOptions={[25]}
            onPageChange={(_, next) => setPage(next)}
            labelDisplayedRows={({ from, to, count }) => `${from}-${to} de ${count}`}
          />
        </Card>
      </DashboardContent>

      <CustomerDrawer base={base} customer={selected} onClose={() => setSelected(null)} />
      <ImportDialog
        base={base}
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={() => {
          setImportOpen(false);
          load();
        }}
      />
    </>
  );
}

// ----------------------------------------------------------------------

function Kpi({ label, value }: { label: string; value: string }) {
  return (
    <Card variant="outlined">
      <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
        <Typography variant="caption" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5">{value}</Typography>
      </CardContent>
    </Card>
  );
}

function CustomerDrawer({ base, customer, onClose }: { base: string; customer: CustomerRow | null; onClose: () => void }) {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [history, setHistory] = useState<Record<string, Snapshot[]>>({});

  useEffect(() => {
    if (!customer) return;
    setLoans([]);
    setHistory({});
    axios.get(`${base}/customers/${customer.partyId}`).then((res) => setLoans(res.data.loans ?? []));
  }, [base, customer]);

  const toggleHistory = async (loanId: string) => {
    if (history[loanId]) {
      setHistory(({ [loanId]: _removed, ...rest }) => rest);
      return;
    }
    const res = await axios.get(`${base}/loans/${loanId}`);
    setHistory((prev) => ({ ...prev, [loanId]: res.data.snapshots ?? [] }));
  };

  return (
    <Drawer anchor="right" open={!!customer} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 520 } } }}>
      {customer && (
        <Box sx={{ p: 3 }}>
          <Typography variant="h6">{customer.name || 'Sin nombre'}</Typography>
          <Typography variant="body2" color="text.secondary">
            {customer.customerExternalId} · {customer.phone || 'sin teléfono'}
          </Typography>
          <Stack direction="row" spacing={1} sx={{ my: 2 }}>
            <Chip label={`${customer.loans} préstamos`} />
            <Chip label={`${money(customer.overdueBalance, customer.currency)} vencido`} color="warning" variant="soft" />
            <Chip label={customer.stageLabel} color={STAGE_COLOR[customer.stage]} variant="soft" />
          </Stack>
          <Divider sx={{ mb: 2 }} />

          <Stack spacing={2}>
            {loans.map((loan) => (
              <Card key={loan.id} variant="outlined">
                <CardContent>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="subtitle1">
                      {loan.loanExternalId} {loan.product ? `· ${loan.product}` : ''}
                    </Typography>
                    <Chip
                      size="small"
                      label={STAGE_LABEL[stageKey(loan.stage)]}
                      color={STAGE_COLOR[stageKey(loan.stage)]}
                      variant="soft"
                    />
                  </Stack>
                  <Typography variant="caption" color="text.secondary">
                    {LOAN_STATUS[loan.status] ?? 'Activo'} · {loan.daysPastDue} días de atraso · corte {day(loan.snapshotDate)}
                    {loan.paymentFrequency ? ` · pago ${loan.paymentFrequency}` : ''}
                  </Typography>

                  <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1, mt: 1.5 }}>
                    <Field label="Saldo total" value={money(loan.totalBalance, loan.currency)} />
                    <Field label="Saldo vencido" value={money(loan.overdueBalance, loan.currency)} />
                    <Field label="Capital vencido" value={money(loan.overduePrincipal, loan.currency)} />
                    <Field label="Interés vencido" value={money(loan.overdueInterest, loan.currency)} />
                    <Field label="Moratorios" value={money(loan.overduePenalties, loan.currency)} />
                    <Field label="Comisiones" value={money(loan.overdueFees, loan.currency)} />
                    <Field
                      label="Próximo pago"
                      value={`${day(loan.nextPaymentDate)} · ${money(loan.nextPaymentAmount, loan.currency)}`}
                    />
                    <Field
                      label="Último pago"
                      value={`${day(loan.lastPaymentDate)} · ${money(loan.lastPaymentAmount, loan.currency)}`}
                    />
                  </Box>

                  <Button size="small" sx={{ mt: 1 }} onClick={() => toggleHistory(loan.id)}>
                    {history[loan.id] ? 'Ocultar historial' : 'Ver historial por corte'}
                  </Button>
                  {history[loan.id] && (
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
                        {history[loan.id].map((s) => (
                          <TableRow key={s.id}>
                            <TableCell>{day(s.snapshotDate)}</TableCell>
                            <TableCell align="right">{s.daysPastDue}</TableCell>
                            <TableCell align="right">{money(s.overdueBalance, loan.currency)}</TableCell>
                            <TableCell align="right">{money(s.totalBalance, loan.currency)}</TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  )}
                </CardContent>
              </Card>
            ))}
          </Stack>
        </Box>
      )}
    </Drawer>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2">{value}</Typography>
    </Box>
  );
}

function ImportDialog({
  base,
  open,
  onClose,
  onImported,
}: {
  base: string;
  open: boolean;
  onClose: () => void;
  onImported: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [snapshotDate, setSnapshotDate] = useState(todayIso());
  const [preview, setPreview] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      setFile(null);
      setPreview(null);
      setError(null);
      setSnapshotDate(todayIso());
    }
  }, [open]);

  const submit = async (dryRun: boolean) => {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      form.append('snapshotDate', snapshotDate);
      form.append('dryRun', String(dryRun));
      const res = await axios.post<ImportResult>(`${base}/imports/csv`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      if (dryRun) setPreview(res.data);
      else onImported();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo procesar el archivo.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Importar cartera</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Un renglón por préstamo. Un cliente puede aparecer en varios renglones (varios préstamos). Volver a cargar un
          préstamo con el mismo id lo actualiza.
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
          <Button variant="outlined" component="label" startIcon={<Iconify icon="mdi:paperclip" />}>
            {file ? file.name : 'Elegir archivo CSV'}
            <input
              hidden
              type="file"
              accept=".csv,text/csv"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                setPreview(null);
              }}
            />
          </Button>
          <TextField
            size="small"
            type="date"
            label="Fecha de corte"
            value={snapshotDate}
            onChange={(e) => {
              setSnapshotDate(e.target.value);
              setPreview(null);
            }}
            InputLabelProps={{ shrink: true }}
          />
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        {preview && (
          <>
            <Alert severity={preview.failed > 0 ? 'warning' : 'success'} sx={{ mb: 2 }}>
              {preview.totalRows} renglones · {preview.validRows} válidos · {preview.customers} clientes ·{' '}
              {preview.loansCreated} préstamos nuevos · {preview.loansUpdated} actualizados
              {preview.failed > 0 ? ` · ${preview.failed} con errores (se omitirán)` : ''}
            </Alert>
            {preview.errors.length > 0 && (
              <TableContainer sx={{ maxHeight: 260 }}>
                <Table size="small" stickyHeader>
                  <TableHead>
                    <TableRow>
                      <TableCell>Renglón</TableCell>
                      <TableCell>Columna</TableCell>
                      <TableCell>Error</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {preview.errors.slice(0, 100).map((e, i) => (
                      <TableRow key={`${e.row}-${e.field}-${i}`}>
                        <TableCell>{e.row}</TableCell>
                        <TableCell>{e.field}</TableCell>
                        <TableCell>{e.message}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="outlined" disabled={!file || busy} onClick={() => submit(true)}>
          Validar
        </Button>
        <Button
          variant="contained"
          disabled={!file || busy || !preview || preview.validRows === 0}
          onClick={() => submit(false)}
        >
          {busy ? <CircularProgress size={18} /> : `Importar ${preview?.validRows ?? ''}`}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
