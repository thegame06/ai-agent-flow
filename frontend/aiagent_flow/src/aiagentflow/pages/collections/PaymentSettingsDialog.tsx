import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Radio from '@mui/material/Radio';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Switch from '@mui/material/Switch';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';
import FormControlLabel from '@mui/material/FormControlLabel';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

type PaymentMethod = {
  id?: string;
  type: 'bank_transfer' | 'cash' | 'app' | 'other';
  name: string;
  accountNumber?: string | null;
  accountHolder?: string | null;
  accountType?: string | null;
  currency?: string | null;
  instructions?: string | null;
  enabled: boolean;
};

type PaymentSettings = {
  methods: PaymentMethod[];
  referenceKind: 'loan' | 'customer';
  notes?: string | null;
};

const TYPES = [
  { key: 'bank_transfer', label: 'Transferencia o depósito' },
  { key: 'cash', label: 'Efectivo (agentes, tiendas)' },
  { key: 'app', label: 'App o billetera' },
  { key: 'other', label: 'Otro' },
];

const emptyMethod = (): PaymentMethod => ({ type: 'bank_transfer', name: '', enabled: true });

type Props = { base: string; open: boolean; onClose: () => void };

/** Where and how customers pay. The collections assistant answers "¿dónde pago?" with this. */
export function PaymentSettingsDialog({ base, open, onClose }: Props) {
  const [settings, setSettings] = useState<PaymentSettings | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (!open) return;
    setSettings(null);
    setError(null);
    setSaved(false);
    axios
      .get<PaymentSettings>(`${base}/payment-settings`)
      .then((res) =>
        setSettings({
          methods: res.data.methods?.length ? res.data.methods : [emptyMethod()],
          referenceKind: res.data.referenceKind === 'customer' ? 'customer' : 'loan',
          notes: res.data.notes ?? '',
        })
      )
      .catch(() => setError('No se pudo cargar la configuración.'));
  }, [base, open]);

  const update = (index: number, patch: Partial<PaymentMethod>) =>
    setSettings((s) => (s ? { ...s, methods: s.methods.map((m, i) => (i === index ? { ...m, ...patch } : m)) } : s));

  const save = async () => {
    if (!settings) return;
    setBusy(true);
    setError(null);
    try {
      await axios.put(`${base}/payment-settings`, settings);
      setSaved(true);
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo guardar.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Medios de pago</DialogTitle>
      <DialogContent>
        {!settings && !error && <LinearProgress />}
        {settings && (
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              El asistente de cobranza usa estos datos cuando un cliente pregunta dónde o cómo pagar. Copia las cuentas tal
              cual: el asistente no las cambia ni inventa otras.
            </Typography>

            {settings.methods.map((m, i) => (
              <Card key={m.id ?? `new-${i}`} variant="outlined" sx={{ p: 2 }}>
                <Stack spacing={1.5}>
                  <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ sm: 'center' }}>
                    <TextField
                      select
                      size="small"
                      label="Tipo"
                      value={m.type}
                      onChange={(e) => update(i, { type: e.target.value as PaymentMethod['type'] })}
                      sx={{ minWidth: 220 }}
                    >
                      {TYPES.map((t) => (
                        <MenuItem key={t.key} value={t.key}>
                          {t.label}
                        </MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      size="small"
                      label="Banco o red"
                      placeholder="BAC, Agentes Banpro..."
                      value={m.name}
                      onChange={(e) => update(i, { name: e.target.value })}
                      fullWidth
                    />
                    <TextField
                      size="small"
                      label="Moneda"
                      placeholder="Cualquiera"
                      value={m.currency ?? ''}
                      onChange={(e) => update(i, { currency: e.target.value })}
                      sx={{ width: 130 }}
                    />
                    <FormControlLabel
                      control={<Switch checked={m.enabled} onChange={(e) => update(i, { enabled: e.target.checked })} />}
                      label="Activo"
                    />
                    <IconButton
                      aria-label="Quitar medio de pago"
                      onClick={() => setSettings((s) => (s ? { ...s, methods: s.methods.filter((_, j) => j !== i) } : s))}
                    >
                      <Iconify icon="mdi:delete-outline" />
                    </IconButton>
                  </Stack>
                  {m.type === 'bank_transfer' && (
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
                      <TextField
                        size="small"
                        label="Número de cuenta"
                        value={m.accountNumber ?? ''}
                        onChange={(e) => update(i, { accountNumber: e.target.value })}
                        fullWidth
                      />
                      <TextField
                        size="small"
                        label="Titular"
                        value={m.accountHolder ?? ''}
                        onChange={(e) => update(i, { accountHolder: e.target.value })}
                        fullWidth
                      />
                      <TextField
                        size="small"
                        label="Tipo de cuenta"
                        placeholder="Ahorro, corriente"
                        value={m.accountType ?? ''}
                        onChange={(e) => update(i, { accountType: e.target.value })}
                        sx={{ minWidth: 160 }}
                      />
                    </Stack>
                  )}
                  <TextField
                    size="small"
                    label="Indicaciones para el cliente"
                    placeholder="Indica tu número de préstamo en el concepto"
                    value={m.instructions ?? ''}
                    onChange={(e) => update(i, { instructions: e.target.value })}
                    fullWidth
                  />
                </Stack>
              </Card>
            ))}

            <Box>
              <Button
                size="small"
                startIcon={<Iconify icon="mdi:plus" />}
                onClick={() => setSettings((s) => (s ? { ...s, methods: [...s.methods, emptyMethod()] } : s))}
              >
                Agregar medio de pago
              </Button>
            </Box>

            <Box>
              <Typography variant="subtitle2">Referencia que debe indicar el cliente</Typography>
              <RadioGroup
                row
                value={settings.referenceKind}
                onChange={(e) => setSettings({ ...settings, referenceKind: e.target.value as PaymentSettings['referenceKind'] })}
              >
                <FormControlLabel value="loan" control={<Radio />} label="Número de préstamo (una por préstamo)" />
                <FormControlLabel value="customer" control={<Radio />} label="Número de cliente" />
              </RadioGroup>
            </Box>

            <TextField
              label="Nota final"
              placeholder="Cuando pagues, envía foto o número del comprobante por este chat."
              value={settings.notes ?? ''}
              onChange={(e) => setSettings({ ...settings, notes: e.target.value })}
              multiline
              minRows={2}
            />
          </Stack>
        )}
        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
        {saved && (
          <Alert severity="success" sx={{ mt: 2 }}>
            Guardado.
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={busy || !settings} onClick={save}>
          Guardar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
