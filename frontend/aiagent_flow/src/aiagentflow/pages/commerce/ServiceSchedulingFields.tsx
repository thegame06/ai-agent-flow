import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import FormControlLabel from '@mui/material/FormControlLabel';

import type { ServiceSchedulingDraft } from './businessHours';

type Props = {
  value: ServiceSchedulingDraft;
  onChange: (next: ServiceSchedulingDraft) => void;
};

/** Booking data of a service: duration, gap between appointments, starting price and preparation. */
export function ServiceSchedulingFields({ value, onChange }: Props) {
  const set = (patch: Partial<ServiceSchedulingDraft>) => onChange({ ...value, ...patch });

  return (
    <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
      <Stack spacing={1.5}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <div>
            <Typography variant="subtitle2">Agenda</Typography>
            <Typography variant="caption" color="text.secondary">
              Cuanto dura el servicio y que necesita. El asistente lo usa para responder y, con la agenda, para reservar.
            </Typography>
          </div>
          <FormControlLabel
            control={<Switch checked={value.bookable} onChange={() => set({ bookable: !value.bookable })} />}
            label="Se agenda"
          />
        </Stack>
        <Grid container spacing={1.5}>
          <Grid item xs={6}>
            <TextField
              fullWidth
              size="small"
              type="number"
              label="Duracion (min)"
              value={value.durationMinutes}
              onChange={(e) => set({ durationMinutes: e.target.value })}
              inputProps={{ min: 0, max: 1440, step: 5 }}
            />
          </Grid>
          <Grid item xs={6}>
            <TextField
              fullWidth
              size="small"
              type="number"
              label="Margen despues (min)"
              helperText="Limpieza o preparacion antes de la siguiente cita"
              value={value.bufferMinutes}
              onChange={(e) => set({ bufferMinutes: e.target.value })}
              inputProps={{ min: 0, max: 240, step: 5 }}
            />
          </Grid>
        </Grid>
        <FormControlLabel
          control={<Switch checked={value.priceIsFrom} onChange={() => set({ priceIsFrom: !value.priceIsFrom })} />}
          label='El precio es "desde" (puede variar)'
        />
        <TextField
          size="small"
          label="Preparacion del cliente"
          placeholder="Venir sin comer 8 horas antes; traer radiografias recientes"
          multiline
          minRows={2}
          value={value.preparation}
          onChange={(e) => set({ preparation: e.target.value })}
        />
        <TextField
          size="small"
          label="Recursos que ocupa"
          placeholder="dentista, sillon"
          helperText="Separados por coma. La agenda los usa para saber quien o que se reserva."
          value={value.resourceKinds}
          onChange={(e) => set({ resourceKinds: e.target.value })}
        />
      </Stack>
    </Card>
  );
}
