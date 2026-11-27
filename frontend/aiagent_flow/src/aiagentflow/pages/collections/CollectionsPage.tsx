import { useNavigate } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Tabs from '@mui/material/Tabs';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import TableContainer from '@mui/material/TableContainer';
import TablePagination from '@mui/material/TablePagination';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';

import axios from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { LoansTab } from './LoansTab';
import { LoanDrawer } from './LoanDrawer';
import { PromisesTab } from './PromisesTab';
import { ImportDialog } from './ImportDialog';
import { CustomerDrawer } from './CustomerDrawer';
import { day, Kpi, money, STAGE_KEYS, STAGE_COLOR, STAGE_LABEL, useDebounced } from './shared';

import type { StageKey } from './shared';

// ----------------------------------------------------------------------

type Summary = {
  customers: number;
  loans: number;
  activeLoans: number;
  totalBalance: number;
  overdueBalance: number;
  currencies: string[];
  byStage: { stage: StageKey; label: string; loans: number; customers: number; overdueBalance: number }[];
  lastImport: { id: string; snapshotDate: string; createdAt: string; source: string; fileName?: string } | null;
  promises: { pending: number; pendingAmount: number; kept: number; broken: number; keptRate: number };
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

type TabKey = 'customers' | 'loans' | 'promises';

// ----------------------------------------------------------------------

export default function CollectionsPage() {
  const tenantId = useTenantId();
  const navigate = useNavigate();
  const base = `/api/v1/tenants/${tenantId}/collections`;

  const [tab, setTab] = useState<TabKey>('customers');
  const [summary, setSummary] = useState<Summary | null>(null);
  const [stage, setStage] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [customerId, setCustomerId] = useState<string | null>(null);
  const [loanId, setLoanId] = useState<string | null>(null);
  const [importOpen, setImportOpen] = useState(false);

  useEffect(() => {
    setError(null);
    axios
      .get<Summary>(`${base}/summary`)
      .then((res) => setSummary(res.data))
      .catch((err) => {
        const code = err?.response?.data?.errorCode;
        setError(
          code === 'APP_MODULE_DISABLED'
            ? 'La app de Cobranza no está habilitada para este cliente. Pide a un administrador que la active.'
            : 'No se pudo cargar la cartera.'
        );
      });
  }, [base, reloadKey]);

  const downloadTemplate = async () => {
    const res = await axios.get(`${base}/imports/template`, { responseType: 'blob' });
    const url = URL.createObjectURL(res.data);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'plantilla-cartera.csv';
    link.click();
    URL.revokeObjectURL(url);
  };

  const campaignForStage = (stageKeyValue: string) =>
    navigate(`${paths.dashboard.campaigns}?audience=collections&stage=${stageKeyValue}`);

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
              {summary?.lastImport
                ? `Corte del ${day(summary.lastImport.snapshotDate)} · última carga ${new Date(summary.lastImport.createdAt).toLocaleString('es-MX')}`
                : 'Aún no hay cargas. Importa tu cartera para empezar.'}
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
            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(5, 1fr)' }, mb: 2 }}>
              <Kpi label="Clientes" value={summary.customers.toLocaleString('es-MX')} />
              <Kpi label="Préstamos activos" value={summary.activeLoans.toLocaleString('es-MX')} hint={`${summary.loans} en total`} />
              <Kpi label="Saldo total" value={money(summary.totalBalance, currency)} />
              <Kpi label="Saldo vencido" value={money(summary.overdueBalance, currency)} />
              <Kpi
                label="Promesas pendientes"
                value={summary.promises.pending.toLocaleString('es-MX')}
                hint={
                  summary.promises.kept + summary.promises.broken > 0
                    ? `${Math.round(summary.promises.keptRate * 100)}% cumplidas · ${money(summary.promises.pendingAmount, currency)}`
                    : money(summary.promises.pendingAmount, currency)
                }
              />
            </Box>

            <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(5, 1fr)' }, mb: 3 }}>
              {summary.byStage.map((s) => (
                <Card
                  key={s.stage}
                  variant="outlined"
                  sx={{
                    borderColor: stage === s.stage ? 'primary.main' : undefined,
                    borderWidth: stage === s.stage ? 2 : 1,
                  }}
                >
                  <CardContent
                    sx={{ py: 1.5, '&:last-child': { pb: 1 }, cursor: 'pointer' }}
                    onClick={() => setStage(stage === s.stage ? '' : s.stage)}
                  >
                    <Chip size="small" label={s.label} color={STAGE_COLOR[s.stage]} variant="soft" sx={{ mb: 1 }} />
                    <Typography variant="h6">{s.loans.toLocaleString('es-MX')} préstamos</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {s.customers} clientes · {money(s.overdueBalance, currency)} vencido
                    </Typography>
                  </CardContent>
                  {s.stage !== 'Current' && s.customers > 0 && (
                    <Button
                      size="small"
                      fullWidth
                      startIcon={<Iconify icon="mdi:bullhorn-outline" />}
                      onClick={() => campaignForStage(s.stage)}
                      sx={{ borderRadius: 0, borderTop: 1, borderColor: 'divider' }}
                    >
                      Crear campaña
                    </Button>
                  )}
                </Card>
              ))}
            </Box>
          </>
        )}

        <Card>
          <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
            <Tab value="customers" label="Clientes" />
            <Tab value="loans" label="Préstamos" />
            <Tab value="promises" label="Promesas de pago" />
          </Tabs>

          {tab === 'customers' && (
            <CustomersTab base={base} stage={stage} onStageChange={setStage} reloadKey={reloadKey} onOpen={setCustomerId} />
          )}
          {tab === 'loans' && <LoansTab base={base} initialStage={stage} onOpenLoan={setLoanId} />}
          {tab === 'promises' && <PromisesTab base={base} onOpenCustomer={setCustomerId} />}
        </Card>
      </DashboardContent>

      <CustomerDrawer base={base} partyId={customerId} onClose={() => setCustomerId(null)} onOpenLoan={setLoanId} />
      <LoanDrawer
        base={base}
        loanId={loanId}
        onClose={() => setLoanId(null)}
        onOpenCustomer={(partyId) => {
          setLoanId(null);
          setCustomerId(partyId);
        }}
      />
      <ImportDialog
        base={base}
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={() => {
          setImportOpen(false);
          setReloadKey((k) => k + 1);
        }}
      />
    </>
  );
}

// ----------------------------------------------------------------------

function CustomersTab({
  base,
  stage,
  onStageChange,
  reloadKey,
  onOpen,
}: {
  base: string;
  stage: string;
  onStageChange: (stage: string) => void;
  reloadKey: number;
  onOpen: (partyId: string) => void;
}) {
  const [customers, setCustomers] = useState<CustomerRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const debouncedQuery = useDebounced(query);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await axios.get(`${base}/customers`, {
        params: { query: debouncedQuery || undefined, stage: stage || undefined, page, pageSize: 25 },
      });
      setCustomers(res.data.items ?? []);
      setTotal(res.data.total ?? 0);
    } catch {
      setCustomers([]);
      setTotal(0);
    } finally {
      setLoading(false);
    }
  }, [base, debouncedQuery, stage, page]);

  useEffect(() => {
    load();
  }, [load, reloadKey]);

  useEffect(() => setPage(0), [debouncedQuery, stage]);

  return (
    <>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2 }}>
        <TextField
          size="small"
          placeholder="Buscar por nombre, teléfono o id de cliente"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          sx={{ flex: 1 }}
        />
        <TextField
          select
          size="small"
          label="Etapa"
          value={stage}
          onChange={(e) => onStageChange(e.target.value)}
          sx={{ minWidth: 180 }}
        >
          <MenuItem value="">Todas</MenuItem>
          {STAGE_KEYS.map((key) => (
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
                <TableRow key={c.partyId} hover sx={{ cursor: 'pointer' }} onClick={() => onOpen(c.partyId)}>
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
    </>
  );
}
