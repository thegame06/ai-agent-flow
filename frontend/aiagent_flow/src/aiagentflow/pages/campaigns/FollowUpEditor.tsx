import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import FormControlLabel from '@mui/material/FormControlLabel';

import type { FollowUpForm, FollowUpStep } from './campaignModel';

// ----------------------------------------------------------------------

type Props = {
  value: FollowUpForm;
  collections: boolean;
  onChange: (value: FollowUpForm) => void;
};

/** What happens after the first message: automatic follow-ups, hand-offs and contact rules. */
export function FollowUpEditor({ value, collections, onChange }: Props) {
  const set = <K extends keyof FollowUpForm>(key: K, v: FollowUpForm[K]) => onChange({ ...value, [key]: v });
  const setStep = (key: 'noResponse' | 'promiseReminder' | 'promiseBroken' | 'callback', patch: Partial<FollowUpStep>) =>
    onChange({ ...value, [key]: { ...value[key], ...patch } });

  return (
    <Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">
        Qué hace la campaña sola después del primer mensaje. Cada seguimiento se envía una vez por cliente y nunca a quien ya
        pagó, pidió no ser contactado o dio un número equivocado.
      </Typography>

      <StepCard
        title="Si no responde"
        hoursLabel="Esperar (horas)"
        step={value.noResponse}
        onChange={(patch) => setStep('noResponse', patch)}
      />
      {collections && (
        <>
          <StepCard
            title="Recordatorio de promesa de pago"
            hoursLabel="Horas antes de la fecha"
            step={value.promiseReminder}
            onChange={(patch) => setStep('promiseReminder', patch)}
            hint="Usa {{monto_promesa}} y {{fecha_promesa}}."
          />
          <StepCard
            title="Si incumple la promesa"
            hoursLabel="Horas después"
            step={value.promiseBroken}
            onChange={(patch) => setStep('promiseBroken', patch)}
          />
        </>
      )}
      <StepCard
        title="Si pide que le escriban después"
        hoursLabel="Esperar (horas)"
        step={value.callback}
        onChange={(patch) => setStep('callback', patch)}
      />

      <Card variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Pasar a una persona
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <FormControlLabel
            control={<Switch checked={value.handoffHuman} onChange={(e) => set('handoffHuman', e.target.checked)} />}
            label="Si pide hablar con un asesor"
          />
          {collections && (
            <FormControlLabel
              control={<Switch checked={value.handoffCannotPay} onChange={(e) => set('handoffCannotPay', e.target.checked)} />}
              label="Si dice que no puede pagar (convenio)"
            />
          )}
        </Stack>
        <Typography variant="caption" color="text.secondary">
          Quedan en la campaña como «Para atender» hasta que alguien registre el resultado.
        </Typography>
        {value.extraRules.length > 0 && (
          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 1 }}>
            <Typography variant="caption" color="text.secondary">
              Reglas adicionales de la estrategia:
            </Typography>
            {value.extraRules.map((r) => (
              <Chip key={r.id} size="small" variant="outlined" label={r.id} />
            ))}
          </Stack>
        )}
      </Card>

      <Card variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Reglas de contacto
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }}>
          <FormControlLabel
            control={<Switch checked={value.windowOn} onChange={(e) => set('windowOn', e.target.checked)} />}
            label="Solo en horario"
          />
          <TextField
            size="small"
            type="number"
            label="Desde (hora)"
            value={value.windowStart}
            disabled={!value.windowOn}
            onChange={(e) => set('windowStart', Math.min(23, Math.max(0, Number(e.target.value))))}
            sx={{ width: 120 }}
          />
          <TextField
            size="small"
            type="number"
            label="Hasta (hora)"
            value={value.windowEnd}
            disabled={!value.windowOn}
            onChange={(e) => set('windowEnd', Math.min(24, Math.max(1, Number(e.target.value))))}
            sx={{ width: 120 }}
          />
          <FormControlLabel
            control={<Switch checked={value.skipSunday} disabled={!value.windowOn} onChange={(e) => set('skipSunday', e.target.checked)} />}
            label="Sin domingos"
          />
        </Stack>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 2 }}>
          <TextField
            size="small"
            type="number"
            label="Máx. mensajes por cliente al día"
            value={value.maxPerDay}
            onChange={(e) => set('maxPerDay', Math.max(0, Number(e.target.value)))}
            helperText="Todas las campañas juntas. 0 = sin límite."
          />
          <TextField
            size="small"
            type="number"
            label="Máx. seguimientos por cliente"
            value={value.maxFollowUps}
            onChange={(e) => set('maxFollowUps', Math.max(0, Number(e.target.value)))}
          />
        </Stack>
        <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 1 }}>
          El horario usa la zona horaria de la campaña. Fuera de horario, los envíos programados y los seguimientos esperan a
          que abra.
        </Typography>
      </Card>
    </Stack>
  );
}

function StepCard({
  title,
  hoursLabel,
  step,
  onChange,
  hint,
}: {
  title: string;
  hoursLabel: string;
  step: FollowUpStep;
  onChange: (patch: Partial<FollowUpStep>) => void;
  hint?: string;
}) {
  return (
    <Card variant="outlined" sx={{ p: 2, opacity: step.on ? 1 : 0.7 }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <FormControlLabel
          control={<Switch checked={step.on} onChange={(e) => onChange({ on: e.target.checked })} />}
          label={<Typography variant="subtitle2">{title}</Typography>}
        />
        <TextField
          size="small"
          type="number"
          label={hoursLabel}
          value={step.hours}
          disabled={!step.on}
          onChange={(e) => onChange({ hours: Math.max(0, Number(e.target.value)) })}
          sx={{ width: 170 }}
        />
      </Stack>
      {step.on && (
        <Box sx={{ mt: 1 }}>
          <TextField
            fullWidth
            size="small"
            multiline
            minRows={2}
            label="Mensaje"
            value={step.message}
            onChange={(e) => onChange({ message: e.target.value })}
            helperText={hint ?? 'Puedes usar los mismos datos del contacto: {{primer_nombre}}, {{total_vencido}}…'}
          />
        </Box>
      )}
    </Card>
  );
}
