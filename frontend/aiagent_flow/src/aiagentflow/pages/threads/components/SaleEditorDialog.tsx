import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';

import axios, { endpoints } from 'src/lib/axios';

type SaleItem = {
  sku: string;
  name: string;
  unitPrice: number;
  quantity: number;
};

type SaleDetail = {
  id: string;
  state: string;
  paymentMethod: string;
  subtotal: number;
  discount: number;
  tax: number;
  total: number;
  currency: string;
  sessionId?: string;
  threadId?: string;
  items: SaleItem[];
};

type Props = {
  open: boolean;
  saleId: string | null;
  tenantId: string;
  onClose: () => void;
  onSaved: () => void;
};

export function SaleEditorDialog({ open, saleId, tenantId, onClose, onSaved }: Props) {
  const [sale, setSale] = useState<SaleDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [paymentMethod, setPaymentMethod] = useState('cash');
  const [applyTax, setApplyTax] = useState(true);
  const [taxRate, setTaxRate] = useState('0.15');
  const [discountAmount, setDiscountAmount] = useState('0');
  const [state, setState] = useState('sale_created');
  const [items, setItems] = useState<SaleItem[]>([]);
  const [preview, setPreview] = useState<{ subtotal: number; discount: number; tax: number; total: number } | null>(null);

  const editable = sale?.state !== 'paid';

  useEffect(() => {
    if (!open || !saleId) return;
    const load = async () => {
      try {
        setLoading(true);
        setError('');
        const res = await axios.get(endpoints.agentflow.commerce.saleById(tenantId, saleId));
        const nextSale = res.data as SaleDetail;
        setSale(nextSale);
        setItems(nextSale.items ?? []);
        setPaymentMethod(nextSale.paymentMethod ?? 'cash');
        setState(nextSale.state ?? 'sale_created');
        setDiscountAmount(String(nextSale.discount ?? 0));
        setApplyTax((nextSale.tax ?? 0) > 0);
        const taxableBase = Math.max((nextSale.subtotal ?? 0) - (nextSale.discount ?? 0), 0);
        setTaxRate(taxableBase > 0 && (nextSale.tax ?? 0) > 0 ? String(Number((nextSale.tax / taxableBase).toFixed(4))) : '0.15');
        setPreview({
          subtotal: nextSale.subtotal,
          discount: nextSale.discount,
          tax: nextSale.tax,
          total: nextSale.total,
        });
      } catch (e: any) {
        setError(e?.message ?? 'No se pudo cargar la venta.');
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [open, saleId, tenantId]);

  useEffect(() => {
    if (!open || !items.length) return;
    const calculate = async () => {
      try {
        const res = await axios.post(endpoints.agentflow.commerce.calculateSale(tenantId), {
          items,
          applyTax,
          taxRate: Number(taxRate || 0),
          discountAmount: Number(discountAmount || 0),
        });
        setPreview(res.data);
      } catch {
        setPreview(null);
      }
    };
    calculate();
  }, [applyTax, discountAmount, items, open, taxRate, tenantId]);

  const canSave = useMemo(() => items.some((item) => item.quantity > 0), [items]);

  const updateQuantity = (sku: string, quantity: number) => {
    setItems((prev) => prev.map((item) => (item.sku === sku ? { ...item, quantity: Math.max(0, quantity) } : item)));
  };

  const save = async () => {
    if (!saleId || !sale || !canSave) return;
    try {
      setSaving(true);
      setError('');
      await axios.put(endpoints.agentflow.commerce.updateSale(tenantId, saleId), {
        sessionId: sale.sessionId,
        threadId: sale.threadId,
        paymentMethod,
        applyTax,
        taxRate: Number(taxRate || 0),
        discountAmount: Number(discountAmount || 0),
        state,
        items,
      });
      onSaved();
      onClose();
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo actualizar la venta.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>Gestionar venta abierta</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          {loading && <Typography color="text.secondary">Cargando venta...</Typography>}
          {!loading && sale && (
            <>
              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <TextField fullWidth select size="small" label="Estado" value={state} onChange={(e) => setState(e.target.value)} disabled={!editable}>
                    <MenuItem value="sale_created">sale_created</MenuItem>
                    <MenuItem value="invoiced">invoiced</MenuItem>
                    <MenuItem value="paid">paid</MenuItem>
                  </TextField>
                </Grid>
                <Grid item xs={12} md={4}>
                  <TextField fullWidth select size="small" label="Metodo de pago" value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)} disabled={!editable}>
                    <MenuItem value="cash">cash</MenuItem>
                    <MenuItem value="card">card</MenuItem>
                    <MenuItem value="transfer">transfer</MenuItem>
                  </TextField>
                </Grid>
                <Grid item xs={12} md={2}>
                  <TextField fullWidth size="small" label="Descuento" value={discountAmount} onChange={(e) => setDiscountAmount(e.target.value)} disabled={!editable} />
                </Grid>
                <Grid item xs={12} md={2}>
                  <TextField fullWidth size="small" label="Tax rate" value={taxRate} onChange={(e) => setTaxRate(e.target.value)} disabled={!editable || !applyTax} />
                </Grid>
              </Grid>

              <Button size="small" variant={applyTax ? 'contained' : 'outlined'} onClick={() => setApplyTax((prev) => !prev)} disabled={!editable} sx={{ alignSelf: 'flex-start' }}>
                {applyTax ? 'IVA activo' : 'IVA inactivo'}
              </Button>

              <Divider />

              <Stack spacing={1.25}>
                {items.map((item) => (
                  <Grid container spacing={1} key={item.sku} alignItems="center">
                    <Grid item xs={12} md={5}>
                      <Typography variant="body2" fontWeight={600}>{item.sku} - {item.name}</Typography>
                    </Grid>
                    <Grid item xs={4} md={2}>
                      <TextField fullWidth size="small" label="Precio" value={item.unitPrice} disabled />
                    </Grid>
                    <Grid item xs={4} md={2}>
                      <TextField fullWidth size="small" label="Cant." type="number" value={item.quantity} onChange={(e) => updateQuantity(item.sku, Number(e.target.value || 0))} disabled={!editable} />
                    </Grid>
                    <Grid item xs={4} md={3}>
                      <Typography variant="body2" textAlign="right">${(item.unitPrice * item.quantity).toFixed(2)}</Typography>
                    </Grid>
                  </Grid>
                ))}
              </Stack>

              <Box sx={{ p: 2, border: '1px solid', borderColor: 'divider', borderRadius: 2, bgcolor: 'background.neutral' }}>
                <Typography variant="subtitle2" sx={{ mb: 1 }}>Resumen recalculado</Typography>
                <Grid container spacing={1}>
                  <Grid item xs={6} md={3}><Typography variant="body2">Subtotal: ${Number(preview?.subtotal ?? 0).toFixed(2)}</Typography></Grid>
                  <Grid item xs={6} md={3}><Typography variant="body2">Descuento: ${Number(preview?.discount ?? 0).toFixed(2)}</Typography></Grid>
                  <Grid item xs={6} md={3}><Typography variant="body2">IVA: ${Number(preview?.tax ?? 0).toFixed(2)}</Typography></Grid>
                  <Grid item xs={6} md={3}><Typography variant="body2" fontWeight={700}>Total: ${Number(preview?.total ?? 0).toFixed(2)}</Typography></Grid>
                </Grid>
              </Box>
            </>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cerrar</Button>
        <Button variant="contained" onClick={save} disabled={!editable || saving || !canSave}>
          Guardar cambios
        </Button>
      </DialogActions>
    </Dialog>
  );
}
