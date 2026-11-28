import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import Autocomplete from '@mui/material/Autocomplete';
import FormControlLabel from '@mui/material/FormControlLabel';

import { Iconify } from 'src/components/iconify';

import { DAYS, copyDay, WEEKDAYS, COMMON_TIMEZONES } from './businessHours';

import type { DayKey, TimeRange, ClosureDraft, BranchHoursDraft } from './businessHours';

// ----------------------------------------------------------------------------

type RangesEditorProps = {
  ranges: TimeRange[];
  onChange: (ranges: TimeRange[]) => void;
};

function RangesEditor({ ranges, onChange }: RangesEditorProps) {
  const update = (index: number, patch: Partial<TimeRange>) =>
    onChange(ranges.map((range, i) => (i === index ? { ...range, ...patch } : range)));

  return (
    <Stack spacing={1}>
      {ranges.map((range, index) => (
        <Stack key={index} direction="row" spacing={1} alignItems="center">
          {/* Plain HH:mm text: a time picker cannot hold "24:00" (open until midnight). */}
          <TextField
            size="small"
            label="Abre"
            placeholder="08:00"
            value={range.open}
            onChange={(e) => update(index, { open: e.target.value.trim() })}
            InputLabelProps={{ shrink: true }}
            inputProps={{ inputMode: 'numeric', maxLength: 5 }}
            sx={{ width: 110 }}
          />
          <TextField
            size="small"
            label="Cierra"
            placeholder="18:00"
            value={range.close}
            onChange={(e) => update(index, { close: e.target.value.trim() })}
            InputLabelProps={{ shrink: true }}
            inputProps={{ inputMode: 'numeric', maxLength: 5 }}
            sx={{ width: 110 }}
          />
          <IconButton aria-label="Quitar horario" size="small" onClick={() => onChange(ranges.filter((_, i) => i !== index))}>
            <Iconify icon="mdi:close" width={18} />
          </IconButton>
        </Stack>
      ))}
      <Button
        size="small"
        sx={{ alignSelf: 'flex-start' }}
        startIcon={<Iconify icon="mdi:plus" width={16} />}
        onClick={() => {
          const last = ranges[ranges.length - 1];
          onChange([...ranges, last ? { open: last.close === '24:00' ? '00:00' : last.close, close: '' } : { open: '08:00', close: '17:00' }]);
        }}
      >
        {ranges.length === 0 ? 'Agregar horario' : 'Agregar otro horario'}
      </Button>
    </Stack>
  );
}

// ----------------------------------------------------------------------------

type Props = {
  value: BranchHoursDraft;
  storeTimezone: string;
  onChange: (next: BranchHoursDraft) => void;
};

/** Weekly hours (several intervals per day = lunch break), holidays and location details of a branch. */
export function BranchHoursEditor({ value, storeTimezone, onChange }: Props) {
  const setDay = (day: DayKey, ranges: TimeRange[]) => onChange({ ...value, week: { ...value.week, [day]: ranges } });
  const setClosure = (index: number, patch: Partial<ClosureDraft>) =>
    onChange({ ...value, closures: value.closures.map((c, i) => (i === index ? { ...c, ...patch } : c)) });

  return (
    <Stack spacing={2}>
      <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
        <Stack spacing={1.5}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <div>
              <Typography variant="subtitle2">Horario de atencion</Typography>
              <Typography variant="caption" color="text.secondary">
                Varios horarios en un dia marcan la pausa de almuerzo. Un dia sin horario queda cerrado.
              </Typography>
            </div>
            <Button
              size="small"
              variant="outlined"
              disabled={value.week.monday.length === 0}
              onClick={() => onChange({ ...value, week: copyDay(value.week, 'monday', WEEKDAYS.filter((d) => d !== 'monday')) })}
            >
              Copiar lunes a viernes
            </Button>
          </Stack>
          {DAYS.map((day) => (
            <Stack key={day.key} direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'flex-start' }}>
              <Typography variant="body2" sx={{ width: 90, pt: { sm: 1 } }}>
                {day.label}
              </Typography>
              <RangesEditor ranges={value.week[day.key]} onChange={(ranges) => setDay(day.key, ranges)} />
            </Stack>
          ))}
        </Stack>
      </Card>

      <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
        <Stack spacing={1.5}>
          <div>
            <Typography variant="subtitle2">Feriados y dias especiales</Typography>
            <Typography variant="caption" color="text.secondary">
              Un dia aqui reemplaza el horario de la semana: cerrado todo el dia o con horario especial.
            </Typography>
          </div>
          {value.closures.map((closure, index) => (
            <Stack key={index} spacing={1} sx={{ pb: 1, borderBottom: '1px dashed', borderColor: 'divider' }}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
                <TextField
                  size="small"
                  type="date"
                  label="Fecha"
                  value={closure.date}
                  onChange={(e) => setClosure(index, { date: e.target.value })}
                  InputLabelProps={{ shrink: true }}
                />
                <TextField
                  size="small"
                  label="Motivo"
                  placeholder="Navidad"
                  value={closure.reason}
                  onChange={(e) => setClosure(index, { reason: e.target.value })}
                  sx={{ flex: 1 }}
                />
                <FormControlLabel
                  control={
                    <Switch
                      checked={closure.closedAllDay}
                      onChange={() => setClosure(index, { closedAllDay: !closure.closedAllDay })}
                    />
                  }
                  label="Cerrado"
                />
                <IconButton
                  aria-label="Quitar fecha"
                  size="small"
                  onClick={() => onChange({ ...value, closures: value.closures.filter((_, i) => i !== index) })}
                >
                  <Iconify icon="mdi:trash-can-outline" width={18} />
                </IconButton>
              </Stack>
              {!closure.closedAllDay && (
                <RangesEditor ranges={closure.intervals} onChange={(intervals) => setClosure(index, { intervals })} />
              )}
            </Stack>
          ))}
          <Button
            size="small"
            sx={{ alignSelf: 'flex-start' }}
            startIcon={<Iconify icon="mdi:calendar-remove-outline" width={16} />}
            onClick={() =>
              onChange({ ...value, closures: [...value.closures, { date: '', reason: '', closedAllDay: true, intervals: [] }] })
            }
          >
            Agregar fecha
          </Button>
        </Stack>
      </Card>

      <Autocomplete
        freeSolo
        options={COMMON_TIMEZONES}
        value={value.timezone}
        onInputChange={(_, timezone) => onChange({ ...value, timezone })}
        renderInput={(params) => (
          <TextField
            {...params}
            label="Zona horaria"
            helperText={`Vacio usa la de la tienda (${storeTimezone}).`}
          />
        )}
      />
      <TextField
        label="Enlace de mapa"
        placeholder="https://maps.app.goo.gl/..."
        value={value.mapsUrl}
        onChange={(e) => onChange({ ...value, mapsUrl: e.target.value })}
      />
      <TextField
        label="Indicaciones para el cliente"
        placeholder="Parqueo frente a la clinica, segundo piso"
        multiline
        minRows={2}
        value={value.notes}
        onChange={(e) => onChange({ ...value, notes: e.target.value })}
      />
    </Stack>
  );
}
