import { useState, useEffect } from 'react';

import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Switch from '@mui/material/Switch';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import FormControlLabel from '@mui/material/FormControlLabel';

import axios from 'src/lib/axios';

// ----------------------------------------------------------------------

/** What a line is for: each purpose keeps its own number, rules and assistant. */
export const LINE_PURPOSES = [
  { value: 'general', label: 'Uso general', hint: 'Cualquier campaña puede usarla.' },
  { value: 'collections', label: 'Cobranza', hint: 'Recordatorios y gestión de pagos. Solo campañas de cobranza.' },
  { value: 'sales', label: 'Ventas', hint: 'Prospectos que ya mostraron interés.' },
  { value: 'marketing', label: 'Marketing', hint: 'Prospectos fríos. Requiere su consentimiento.' },
] as const;

export const purposeLabel = (purpose?: string | null) =>
  LINE_PURPOSES.find((p) => p.value === (purpose || 'general'))?.label ?? 'Uso general';

type LineChannel = { id: string; name: string; type: string; config?: Record<string, string> | null };

type LineForm = { phoneNumber: string; purpose: string; defaultLine: boolean; contactHours: string; timeZone: string };

const formOf = (channel: LineChannel): LineForm => ({
  phoneNumber: channel.config?.PhoneNumber ?? '',
  purpose: channel.config?.Purpose || 'general',
  defaultLine: (channel.config?.DefaultLine ?? '').toLowerCase() === 'true',
  contactHours: channel.config?.ContactHours ?? '',
  timeZone: channel.config?.TimeZone ?? 'America/Mexico_City',
});

/** The line's chips in the channels table: purpose, number and whether it is the default one. */
export function LineChips({ channel }: { channel: LineChannel }) {
  const form = formOf(channel);
  const hasLine = form.phoneNumber || form.purpose !== 'general' || form.defaultLine;
  if (!hasLine) return null;
  return (
    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
      <Chip size="small" variant="outlined" label={purposeLabel(form.purpose)} />
      {form.phoneNumber && <Chip size="small" variant="outlined" label={form.phoneNumber} />}
      {form.defaultLine && <Chip size="small" color="info" variant="outlined" label="Predeterminada" />}
    </Stack>
  );
}

/**
 * A channel as a line: its business number (the caller id, and how inbound calls and messages find
 * it), purpose, whether it is the default line of its kind and the hours customers may be contacted.
 */
export function LineDialog({
  tenantId,
  channel,
  onClose,
  onSaved,
}: {
  tenantId: string;
  channel: LineChannel | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<LineForm | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setError(null);
    setForm(channel ? formOf(channel) : null);
  }, [channel]);

  if (!channel || !form) return null;

  const set = <K extends keyof LineForm>(key: K, value: LineForm[K]) => setForm({ ...form, [key]: value });
  const purpose = LINE_PURPOSES.find((p) => p.value === form.purpose);
  const isMetaWhatsApp = channel.type === 'WhatsApp' && (channel.config?.AuthMode ?? 'qr') === 'business';

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      await axios.put(`/api/v1/tenants/${tenantId}/channels/${channel.id}/line`, {
        phoneNumber: form.phoneNumber.trim(),
        purpose: form.purpose,
        defaultLine: form.defaultLine,
        contactHours: form.contactHours.trim(),
        timeZone: form.timeZone.trim(),
      });
      onSaved();
      onClose();
    } catch (e: any) {
      setError(e?.response?.data?.message ?? e?.message ?? 'No se pudo guardar la línea.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Línea: {channel.name}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            Cada número es una línea con su propósito y su asistente. Lo que entra por este número lo
            atiende esta línea, y lo que sale por ella usa este número.
          </Typography>
          <TextField
            label="Número de la línea"
            placeholder="+525512345678"
            value={form.phoneNumber}
            onChange={(e) => set('phoneNumber', e.target.value)}
            helperText={
              isMetaWhatsApp
                ? 'En WhatsApp de Meta la línea se identifica por el id del número (cuenta de Meta); aquí es solo informativo.'
                : 'Con código de país. Vacío: usa el número de la integración.'
            }
          />
          <TextField select label="Propósito" value={form.purpose} onChange={(e) => set('purpose', e.target.value)} helperText={purpose?.hint}>
            {LINE_PURPOSES.map((p) => (
              <MenuItem key={p.value} value={p.value}>
                {p.label}
              </MenuItem>
            ))}
          </TextField>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              fullWidth
              label="Horario de contacto"
              placeholder="08:00-21:00"
              value={form.contactHours}
              onChange={(e) => set('contactHours', e.target.value)}
              helperText="Las campañas solo contactan dentro de este horario. Vacío: a cualquier hora."
            />
            <TextField
              fullWidth
              label="Zona horaria"
              value={form.timeZone}
              onChange={(e) => set('timeZone', e.target.value)}
              helperText="Por ejemplo America/Mexico_City."
            />
          </Stack>
          <FormControlLabel
            control={<Switch checked={form.defaultLine} onChange={(e) => set('defaultLine', e.target.checked)} />}
            label="Línea predeterminada de su tipo (atiende los números que no son de otra línea)"
          />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" onClick={save} disabled={saving}>
          Guardar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
