import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';

type CartItem = {
  sku: string;
  name: string;
  unitPrice: number;
  quantity: number;
};

type Calculation = {
  subtotal: number;
  discount: number;
  tax: number;
  total: number;
};

type Props = {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  customerName: string;
  currency: string;
  items: CartItem[];
  calculation: Calculation | null;
  disabled?: boolean;
};

export function InvoicePreviewDialog({ open, onClose, onConfirm, customerName, currency, items, calculation, disabled }: Props) {
  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>Vista previa de factura</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Box sx={{ p: 2, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
            <Typography variant="overline" color="text.secondary">Cliente</Typography>
            <Typography variant="h6">{customerName || 'Sin identificar'}</Typography>
          </Box>

          <Box sx={{ p: 2, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
            <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Detalle</Typography>
            <Stack spacing={1.2}>
              {items.map((item) => (
                <Grid container spacing={1} key={item.sku}>
                  <Grid item xs={7}>
                    <Typography variant="body2">{item.quantity} x {item.name}</Typography>
                  </Grid>
                  <Grid item xs={5}>
                    <Typography variant="body2" textAlign="right">
                      {currency} {(item.unitPrice * item.quantity).toFixed(2)}
                    </Typography>
                  </Grid>
                </Grid>
              ))}
            </Stack>
            <Divider sx={{ my: 1.5 }} />
            <Grid container spacing={1}>
              <Grid item xs={6}><Typography variant="body2">Subtotal</Typography></Grid>
              <Grid item xs={6}><Typography variant="body2" textAlign="right">{currency} {Number(calculation?.subtotal ?? 0).toFixed(2)}</Typography></Grid>
              <Grid item xs={6}><Typography variant="body2">Descuento</Typography></Grid>
              <Grid item xs={6}><Typography variant="body2" textAlign="right">{currency} {Number(calculation?.discount ?? 0).toFixed(2)}</Typography></Grid>
              <Grid item xs={6}><Typography variant="body2">Impuesto</Typography></Grid>
              <Grid item xs={6}><Typography variant="body2" textAlign="right">{currency} {Number(calculation?.tax ?? 0).toFixed(2)}</Typography></Grid>
              <Grid item xs={6}><Typography variant="subtitle2">Total</Typography></Grid>
              <Grid item xs={6}><Typography variant="subtitle2" textAlign="right">{currency} {Number(calculation?.total ?? 0).toFixed(2)}</Typography></Grid>
            </Grid>
          </Box>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" onClick={onConfirm} disabled={disabled}>
          Emitir factura
        </Button>
      </DialogActions>
    </Dialog>
  );
}
