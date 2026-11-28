import { useMemo, useState, useEffect } from 'react';

import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import Autocomplete from '@mui/material/Autocomplete';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';

import axios, { endpoints } from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { type Frequency, FREQUENCY_LABEL, buildInstallments } from './installments';

type Party = { id: string; displayName?: string; fullName?: string; phone?: string; identifier: string };
type CatalogItem = { sku: string; name: string; itemType: string; unitPrice: number; onHand: number; tracksInventory: boolean; active: boolean };
type Line = { sku: string; quantity: number };

type Props = {
  tenantId: string;
  open: boolean;
  currency: string;
  taxRate: number;
  onClose: () => void;
  onCreated: (sale: { id: string; total: number; currency: string; paymentMethod: string }) => void;
};

const partyLabel = (p: Party) => [p.fullName || p.displayName || p.identifier, p.phone].filter(Boolean).join(' · ');

/**
 * Registers a sale from the catalog, paid in cash or in installments. A sale on installments opens
 * the customer's account in collections, so its reminders and payment reports work like any loan's.
 */
export function NewSaleDialog({ tenantId, open, currency, taxRate, onClose, onCreated }: Props) {
  const [parties, setParties] = useState<Party[]>([]);
  const [partyQuery, setPartyQuery] = useState('');
  const [party, setParty] = useState<Party | null>(null);
  const [catalog, setCatalog] = useState<CatalogItem[]>([]);
  const [lines, setLines] = useState<Line[]>([]);
  const [method, setMethod] = useState<'cash' | 'card' | 'transfer' | 'installments'>('cash');
  const [count, setCount] = useState(3);
  const [frequency, setFrequency] = useState<Frequency>('monthly');
  const [downPayment, setDownPayment] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setParty(null);
    setLines([]);
    setMethod('cash');
    setCount(3);
    setFrequency('monthly');
    setDownPayment(0);
    setError(null);
    axios
      .get(`${endpoints.agentflow.commerce.inventorySearch(tenantId)}?limit=100`)
      .then((res) => setCatalog(((res.data ?? []) as CatalogItem[]).filter((i) => i.active)))
      .catch(() => setError('No se pudo cargar el catálogo.'));
  }, [open, tenantId]);

  useEffect(() => {
    if (!open) return undefined;
    const handle = setTimeout(() => {
      const qs = new URLSearchParams({ page: '0', pageSize: '20' });
      if (partyQuery.trim()) qs.set('query', partyQuery.trim());
      axios
        .get(`${endpoints.agentflow.commerce.customers(tenantId)}?${qs.toString()}`)
        .then((res) => setParties(res.data?.items ?? []))
        .catch(() => undefined);
    }, 250);
    return () => clearTimeout(handle);
  }, [open, partyQuery, tenantId]);

  const priced = useMemo(
    () =>
      lines
        .map((line) => ({ line, item: catalog.find((i) => i.sku === line.sku) }))
        .filter((x): x is { line: Line; item: CatalogItem } => !!x.item),
    [lines, catalog]
  );
  const subtotal = priced.reduce((acc, { line, item }) => acc + item.unitPrice * line.quantity, 0);
  // The store's rate may be typed as a percentage ("15") or a fraction (0.15), as the API reads it.
  const taxFraction = taxRate > 1 ? taxRate / 100 : Math.max(0, taxRate);
  const tax = Math.round(subtotal * taxFraction * 100) / 100;
  const total = subtotal + tax;
  const schedule = useMemo(
    () => (method === 'installments' && total > 0 ? buildInstallments(total, count, frequency, downPayment, new Date()) : []),
    [method, total, count, frequency, downPayment]
  );

  const stockProblem = priced.find(({ line, item }) => item.tracksInventory && item.onHand < line.quantity);
  const planProblem =
    method !== 'installments'
      ? null
      : count < 2 || count > 60
        ? 'El número de cuotas debe estar entre 2 y 60.'
        : downPayment < 0 || downPayment >= total
          ? 'La prima debe ser menor que el total.'
          : null;
  const canSave = !!party && priced.length > 0 && !stockProblem && !planProblem && !busy;

  const save = async () => {
    if (!party) return;
    setBusy(true);
    setError(null);
    try {
      const res = await axios.post(endpoints.agentflow.commerce.createSale(tenantId), {
        partyId: party.id,
        paymentMethod: method,
        installments: method === 'installments' ? { count, frequency, downPayment } : undefined,
        items: priced.map(({ line }) => ({ sku: line.sku, quantity: line.quantity })),
      });
      onCreated(res.data);
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? err?.message ?? 'No se pudo registrar la venta.');
    } finally {
      setBusy(false);
    }
  };

  const money = (value: number) => `${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Nueva venta</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} sx={{ pt: 1 }}>
          <Autocomplete
            options={parties}
            value={party}
            onChange={(_, value) => setParty(value)}
            onInputChange={(_, value) => setPartyQuery(value)}
            getOptionLabel={partyLabel}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            filterOptions={(x) => x}
            renderInput={(params) => <TextField {...params} label="Cliente" placeholder="Nombre o teléfono" />}
          />

          <Stack spacing={1}>
            <Typography variant="subtitle2">Productos y servicios</Typography>
            {lines.map((line, index) => {
              const item = catalog.find((i) => i.sku === line.sku);
              return (
                <Stack key={index} direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
                  <TextField
                    select
                    size="small"
                    label="Producto o servicio"
                    value={line.sku}
                    sx={{ flex: 1 }}
                    onChange={(e) => setLines((prev) => prev.map((l, i) => (i === index ? { ...l, sku: e.target.value } : l)))}
                  >
                    {catalog.map((i) => (
                      <MenuItem key={i.sku} value={i.sku}>
                        {i.name} — {money(i.unitPrice)}
                        {i.tracksInventory ? ` (${i.onHand} en stock)` : ' (servicio)'}
                      </MenuItem>
                    ))}
                  </TextField>
                  <TextField
                    size="small"
                    type="number"
                    label="Cantidad"
                    value={line.quantity}
                    inputProps={{ min: 1 }}
                    sx={{ width: { sm: 110 } }}
                    onChange={(e) =>
                      setLines((prev) => prev.map((l, i) => (i === index ? { ...l, quantity: Math.max(1, Number(e.target.value) || 1) } : l)))
                    }
                  />
                  <Typography variant="body2" sx={{ width: { sm: 130 }, textAlign: { sm: 'right' } }}>
                    {item ? money(item.unitPrice * line.quantity) : '—'}
                  </Typography>
                  <IconButton aria-label="Quitar" onClick={() => setLines((prev) => prev.filter((_, i) => i !== index))}>
                    <Iconify icon="mingcute:close-line" />
                  </IconButton>
                </Stack>
              );
            })}
            <Button
              size="small"
              startIcon={<Iconify icon="mingcute:add-line" />}
              sx={{ alignSelf: 'flex-start' }}
              disabled={catalog.length === 0}
              onClick={() => setLines((prev) => [...prev, { sku: catalog[0]?.sku ?? '', quantity: 1 }])}
            >
              Agregar
            </Button>
            {catalog.length === 0 && <Alert severity="info">El catálogo está vacío: agrega productos o servicios en Inventario.</Alert>}
            {stockProblem && <Alert severity="warning">No hay stock suficiente de {stockProblem.item.name}.</Alert>}
          </Stack>

          <Divider />

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField select size="small" label="Forma de pago" value={method} onChange={(e) => setMethod(e.target.value as typeof method)} sx={{ minWidth: 200 }}>
              <MenuItem value="cash">Contado (efectivo)</MenuItem>
              <MenuItem value="card">Contado (tarjeta)</MenuItem>
              <MenuItem value="transfer">Contado (transferencia)</MenuItem>
              <MenuItem value="installments">En cuotas</MenuItem>
            </TextField>
            {method === 'installments' && (
              <>
                <TextField size="small" type="number" label="Cuotas" value={count} inputProps={{ min: 2, max: 60 }} onChange={(e) => setCount(Number(e.target.value) || 0)} sx={{ width: 110 }} />
                <TextField select size="small" label="Frecuencia" value={frequency} onChange={(e) => setFrequency(e.target.value as Frequency)} sx={{ minWidth: 140 }}>
                  {(Object.keys(FREQUENCY_LABEL) as Frequency[]).map((f) => (
                    <MenuItem key={f} value={f}>
                      {FREQUENCY_LABEL[f]}
                    </MenuItem>
                  ))}
                </TextField>
                <TextField size="small" type="number" label="Prima" value={downPayment} inputProps={{ min: 0 }} onChange={(e) => setDownPayment(Number(e.target.value) || 0)} sx={{ width: 140 }} />
              </>
            )}
          </Stack>

          <Stack spacing={0.5} alignItems="flex-end">
            <Typography variant="body2" color="text.secondary">Subtotal: {money(subtotal)}</Typography>
            {tax > 0 && <Typography variant="body2" color="text.secondary">Impuesto: {money(tax)}</Typography>}
            <Typography variant="h6">Total: {money(total)}</Typography>
          </Stack>

          {planProblem && <Alert severity="warning">{planProblem}</Alert>}
          {schedule.length > 0 && !planProblem && (
            <Stack spacing={1}>
              <Typography variant="subtitle2">
                Plan: prima de {money(downPayment)} y {count} cuotas {FREQUENCY_LABEL[frequency].toLowerCase()}es. Se abre la cuenta del cliente en Cobranza para recordarle cada pago.
              </Typography>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Cuota</TableCell>
                    <TableCell>Vence</TableCell>
                    <TableCell align="right">Monto</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {schedule.map((s) => (
                    <TableRow key={s.number}>
                      <TableCell>{s.number}</TableCell>
                      <TableCell>{s.dueDate.toLocaleDateString()}</TableCell>
                      <TableCell align="right">{money(s.amount)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Stack>
          )}

          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={!canSave} onClick={save}>
          Registrar venta
        </Button>
      </DialogActions>
    </Dialog>
  );
}
