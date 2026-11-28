import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import FormControlLabel from '@mui/material/FormControlLabel';

import { changeMode, PRICE_UNITS, BOOKING_MODES } from './booking';

import type { PriceUnit, BookingMode, BookingDraft } from './booking';

type Props = {
  value: BookingDraft;
  onChange: (next: BookingDraft) => void;
};

const RANGE_LABELS: Record<'daily' | 'nightly', { unit: string; start: string; end: string }> = {
  daily: { unit: 'dias', start: 'Hora de entrega', end: 'Hora de devolucion' },
  nightly: { unit: 'noches', start: 'Hora de entrada', end: 'Hora de salida' },
};

/** How an item (service or tangible) is reserved: a doctor, a table, a room, a house, a machine. */
export function BookingFields({ value, onChange }: Props) {
  const set = (patch: Partial<BookingDraft>) => onChange({ ...value, ...patch });
  const mode = BOOKING_MODES.find((m) => m.value === value.mode) ?? BOOKING_MODES[0];
  const range = value.mode === 'daily' || value.mode === 'nightly' ? RANGE_LABELS[value.mode] : null;

  return (
    <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
      <Stack spacing={1.5}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <div>
            <Typography variant="subtitle2">Reservas</Typography>
            <Typography variant="caption" color="text.secondary">
              Si se puede reservar (una cita, una mesa, una habitacion, una casa, una maquina), como se mide el tiempo y para cuantas personas.
            </Typography>
          </div>
          <FormControlLabel
            control={<Switch checked={value.bookable} onChange={() => set({ bookable: !value.bookable })} />}
            label="Se reserva"
          />
        </Stack>

        {value.bookable && (
          <>
            <TextField
              select
              size="small"
              label="Como se reserva"
              value={value.mode}
              onChange={(e) => onChange(changeMode(value, e.target.value as BookingMode))}
              helperText={`Ejemplos: ${mode.examples}`}
            >
              {BOOKING_MODES.map((option) => (
                <MenuItem key={option.value} value={option.value}>
                  {option.label}
                </MenuItem>
              ))}
            </TextField>

            <Grid container spacing={1.5}>
              {value.mode === 'appointment' && (
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
              )}
              {value.mode === 'hourly' && (
                <>
                  <Grid item xs={6}>
                    <TextField
                      fullWidth
                      size="small"
                      type="number"
                      label="Minimo de horas"
                      value={value.minHours}
                      onChange={(e) => set({ minHours: e.target.value })}
                      inputProps={{ min: 0, step: 0.5 }}
                    />
                  </Grid>
                  <Grid item xs={6}>
                    <TextField
                      fullWidth
                      size="small"
                      type="number"
                      label="Maximo de horas"
                      helperText="Vacio = sin maximo"
                      value={value.maxHours}
                      onChange={(e) => set({ maxHours: e.target.value })}
                      inputProps={{ min: 0, step: 0.5 }}
                    />
                  </Grid>
                </>
              )}
              {range && (
                <>
                  <Grid item xs={6}>
                    <TextField
                      fullWidth
                      size="small"
                      type="number"
                      label={`Minimo de ${range.unit}`}
                      value={value.minUnits}
                      onChange={(e) => set({ minUnits: e.target.value })}
                      inputProps={{ min: 1 }}
                    />
                  </Grid>
                  <Grid item xs={6}>
                    <TextField
                      fullWidth
                      size="small"
                      type="number"
                      label={`Maximo de ${range.unit}`}
                      helperText="Vacio = sin maximo"
                      value={value.maxUnits}
                      onChange={(e) => set({ maxUnits: e.target.value })}
                      inputProps={{ min: 0 }}
                    />
                  </Grid>
                  <Grid item xs={6}>
                    <TextField
                      fullWidth
                      size="small"
                      label={range.start}
                      placeholder="15:00"
                      value={value.startTime}
                      onChange={(e) => set({ startTime: e.target.value.trim() })}
                      inputProps={{ maxLength: 5 }}
                    />
                  </Grid>
                  <Grid item xs={6}>
                    <TextField
                      fullWidth
                      size="small"
                      label={range.end}
                      placeholder="12:00"
                      value={value.endTime}
                      onChange={(e) => set({ endTime: e.target.value.trim() })}
                      inputProps={{ maxLength: 5 }}
                    />
                  </Grid>
                </>
              )}
              <Grid item xs={6}>
                <TextField
                  fullWidth
                  size="small"
                  type="number"
                  label="Bloqueo despues (min)"
                  helperText="Limpieza, mantenimiento o preparacion"
                  value={value.bufferMinutes}
                  onChange={(e) => set({ bufferMinutes: e.target.value })}
                  inputProps={{ min: 0, max: 1440, step: 5 }}
                />
              </Grid>
              <Grid item xs={6}>
                <TextField
                  select
                  fullWidth
                  size="small"
                  label="El precio es"
                  value={value.priceUnit}
                  onChange={(e) => set({ priceUnit: e.target.value as PriceUnit })}
                >
                  {PRICE_UNITS.map((unit) => (
                    <MenuItem key={unit.value} value={unit.value}>
                      {unit.label}
                    </MenuItem>
                  ))}
                </TextField>
              </Grid>
              <Grid item xs={6}>
                <TextField
                  fullWidth
                  size="small"
                  type="number"
                  label="Personas minimo"
                  value={value.minPartySize}
                  onChange={(e) => set({ minPartySize: e.target.value })}
                  inputProps={{ min: 0 }}
                />
              </Grid>
              <Grid item xs={6}>
                <TextField
                  fullWidth
                  size="small"
                  type="number"
                  label="Personas maximo"
                  helperText="Para mesas o habitaciones; vacio si no aplica"
                  value={value.maxPartySize}
                  onChange={(e) => set({ maxPartySize: e.target.value })}
                  inputProps={{ min: 0 }}
                />
              </Grid>
            </Grid>
          </>
        )}

        <FormControlLabel
          control={<Switch checked={value.priceIsFrom} onChange={() => set({ priceIsFrom: !value.priceIsFrom })} />}
          label='El precio es "desde" (puede variar)'
        />
        {value.bookable && (
          <TextField
            size="small"
            label="Que se reserva"
            placeholder="dentista, sillon / mesa / habitacion-doble / retroexcavadora"
            helperText="Tipos de recurso que ocupa una reserva, separados por coma. La agenda los usa para saber que asignar."
            value={value.resourceKinds}
            onChange={(e) => set({ resourceKinds: e.target.value })}
          />
        )}
        <TextField
          size="small"
          label="Indicaciones para el cliente"
          placeholder="Venir sin comer; traer licencia de conducir; no se admiten mascotas"
          multiline
          minRows={2}
          value={value.instructions}
          onChange={(e) => set({ instructions: e.target.value })}
        />
      </Stack>
    </Card>
  );
}
