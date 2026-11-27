import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import TableSortLabel from '@mui/material/TableSortLabel';
import TablePagination from '@mui/material/TablePagination';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';

import {
  day,
  money,
  STAGE_KEYS,
  STAGE_COLOR,
  STAGE_LABEL,
  useDebounced,
  LOAN_STATUS_KEYS,
  LOAN_STATUS_LABEL,
} from './shared';

import type { StageKey } from './shared';

// ----------------------------------------------------------------------

type LoanRow = {
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
  totalBalance: number;
  overdueBalance: number;
  daysPastDue: number;
  stage: StageKey;
  stageLabel: string;
  status: string;
  nextPaymentDate?: string;
  nextPaymentAmount?: number;
  lastPaymentDate?: string;
};

type LoansResponse = {
  total: number;
  totalBalance: number;
  overdueBalance: number;
  products: string[];
  items: LoanRow[];
};

const COLUMNS: { key: string; label: string; align?: 'right' }[] = [
  { key: 'loan', label: 'Préstamo' },
  { key: 'customer', label: 'Cliente' },
  { key: 'overdue', label: 'Saldo vencido', align: 'right' },
  { key: 'balance', label: 'Saldo total', align: 'right' },
  { key: 'dpd', label: 'Días', align: 'right' },
  { key: 'nextpayment', label: 'Próximo pago' },
];

type Props = {
  base: string;
  initialStage?: string;
  onOpenLoan: (loanId: string) => void;
};

export function LoansTab({ base, initialStage = '', onOpenLoan }: Props) {
  const [query, setQuery] = useState('');
  const [stage, setStage] = useState(initialStage);
  const [status, setStatus] = useState('');
  const [product, setProduct] = useState('');
  const [sort, setSort] = useState('dpd');
  const [desc, setDesc] = useState(true);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [data, setData] = useState<LoansResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const debouncedQuery = useDebounced(query);

  useEffect(() => setStage(initialStage), [initialStage]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    axios
      .get<LoansResponse>(`${base}/loans`, {
        params: {
          query: debouncedQuery || undefined,
          stage: stage || undefined,
          status: status || undefined,
          product: product || undefined,
          sort,
          desc,
          page,
          pageSize,
        },
      })
      .then((res) => !cancelled && setData(res.data))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [base, debouncedQuery, stage, status, product, sort, desc, page, pageSize]);

  const toggleSort = (key: string) => {
    if (sort === key) setDesc(!desc);
    else {
      setSort(key);
      setDesc(key !== 'customer' && key !== 'loan' && key !== 'nextpayment');
    }
    setPage(0);
  };

  const resetPage = <T,>(setter: (v: T) => void) => (value: T) => {
    setPage(0);
    setter(value);
  };

  return (
    <>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ p: 2 }}>
        <TextField
          size="small"
          placeholder="Buscar por id de préstamo, cliente o teléfono"
          value={query}
          onChange={(e) => resetPage(setQuery)(e.target.value)}
          sx={{ flex: 1 }}
        />
        <TextField select size="small" label="Etapa" value={stage} onChange={(e) => resetPage(setStage)(e.target.value)} sx={{ minWidth: 150 }}>
          <MenuItem value="">Todas</MenuItem>
          {STAGE_KEYS.map((key) => (
            <MenuItem key={key} value={key}>
              {STAGE_LABEL[key]}
            </MenuItem>
          ))}
        </TextField>
        <TextField select size="small" label="Estado" value={status} onChange={(e) => resetPage(setStatus)(e.target.value)} sx={{ minWidth: 150 }}>
          <MenuItem value="">Todos</MenuItem>
          {LOAN_STATUS_KEYS.map((key) => (
            <MenuItem key={key} value={key}>
              {LOAN_STATUS_LABEL[key]}
            </MenuItem>
          ))}
        </TextField>
        <TextField select size="small" label="Producto" value={product} onChange={(e) => resetPage(setProduct)(e.target.value)} sx={{ minWidth: 160 }}>
          <MenuItem value="">Todos</MenuItem>
          {(data?.products ?? []).map((p) => (
            <MenuItem key={p} value={p}>
              {p}
            </MenuItem>
          ))}
        </TextField>
      </Stack>

      {data && (
        <Typography variant="body2" color="text.secondary" sx={{ px: 2, pb: 1 }}>
          {data.total.toLocaleString('es-MX')} préstamos · vencido {money(data.overdueBalance)} · saldo total {money(data.totalBalance)}
        </Typography>
      )}

      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              {COLUMNS.map((c) => (
                <TableCell key={c.key} align={c.align}>
                  <TableSortLabel active={sort === c.key} direction={desc ? 'desc' : 'asc'} onClick={() => toggleSort(c.key)}>
                    {c.label}
                  </TableSortLabel>
                </TableCell>
              ))}
              <TableCell>Etapa</TableCell>
              <TableCell>Estado</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={8} align="center" sx={{ py: 4 }}>
                  <CircularProgress size={24} />
                </TableCell>
              </TableRow>
            )}
            {!loading && data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={8} align="center" sx={{ py: 4, color: 'text.secondary' }}>
                  No hay préstamos con estos filtros.
                </TableCell>
              </TableRow>
            )}
            {!loading &&
              data?.items.map((l) => (
                <TableRow key={l.id} hover sx={{ cursor: 'pointer' }} onClick={() => onOpenLoan(l.id)}>
                  <TableCell>
                    <Typography variant="subtitle2">{l.loanExternalId}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {l.product || '—'} · {money(l.originalAmount, l.currency)}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">{l.customerName || 'Sin nombre'}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {l.customerExternalId} · {l.customerPhone || 'sin teléfono'}
                    </Typography>
                  </TableCell>
                  <TableCell align="right">{money(l.overdueBalance, l.currency)}</TableCell>
                  <TableCell align="right">{money(l.totalBalance, l.currency)}</TableCell>
                  <TableCell align="right">{l.daysPastDue}</TableCell>
                  <TableCell>
                    <Box>{day(l.nextPaymentDate)}</Box>
                    <Typography variant="caption" color="text.secondary">
                      {money(l.nextPaymentAmount, l.currency)}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Chip size="small" label={l.stageLabel} color={STAGE_COLOR[l.stage]} variant="soft" />
                  </TableCell>
                  <TableCell>{LOAN_STATUS_LABEL[l.status] ?? l.status}</TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </TableContainer>
      <TablePagination
        component="div"
        count={data?.total ?? 0}
        page={page}
        rowsPerPage={pageSize}
        rowsPerPageOptions={[25, 50, 100]}
        onPageChange={(_, next) => setPage(next)}
        onRowsPerPageChange={(e) => {
          setPageSize(Number(e.target.value));
          setPage(0);
        }}
        labelRowsPerPage="Por página"
        labelDisplayedRows={({ from, to, count }) => `${from}-${to} de ${count}`}
      />
    </>
  );
}
