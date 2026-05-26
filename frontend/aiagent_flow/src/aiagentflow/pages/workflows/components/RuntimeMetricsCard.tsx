import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { activityTypeLabel } from '../constants';

import type { WorkflowAuditEvent, AssistantWizardMetrics, WorkflowRuntimeMetrics } from '../types';

type Props = {
  metrics: WorkflowRuntimeMetrics | null;
  wizardMetrics: AssistantWizardMetrics | null;
  auditEvents: WorkflowAuditEvent[];
};

const PROVIDER_ROLES = [
  { key: 'stt', label: 'STT' },
  { key: 'tts', label: 'TTS' },
  { key: 'callControl', label: 'CallControl' },
  { key: 'reasoning', label: 'Reasoning' },
] as const;

export function RuntimeMetricsCard({ metrics, wizardMetrics, auditEvents }: Props) {
  const completionRate = wizardMetrics?.conversion?.completionRate ?? 0;
  const materializationRate = wizardMetrics?.conversion?.materializationRate ?? 0;
  const completionAlert = completionRate < 0.6;
  const materializationAlert = materializationRate < 0.4;

  return (
    <Card sx={{ p: 2 }}>
      <Typography variant="h6" sx={{ mb: 2 }}>
        Metricas de ejecucion
      </Typography>
      {!metrics ? (
        <Alert severity="info">Metricas no disponibles.</Alert>
      ) : (
        <Stack spacing={1.2}>
          <Typography variant="body2">
            Total: <strong>{metrics.total ?? 0}</strong>
          </Typography>
          <Typography variant="body2">
            Tasa de exito: <strong>{Math.round((metrics.successRate ?? 0) * 100)}%</strong>
          </Typography>
          <Typography variant="body2">
            Tasa de fallo: <strong>{Math.round((metrics.failureRate ?? 0) * 100)}%</strong>
          </Typography>
          <Typography variant="body2">
            Latencia promedio: <strong>{metrics.avgLatencyMs ?? 0} ms</strong>
          </Typography>
          {metrics.window && (
            <Typography variant="caption" color="text.secondary">
              Ventana: {metrics.window}
            </Typography>
          )}

          {metrics.continuitySignals && (
            <Stack spacing={0.8}>
              <Stack direction="row" spacing={1} flexWrap="wrap">
                <Chip
                  size="small"
                  label={`Loops: ${metrics.continuitySignals.loopDetected}`}
                  color={metrics.continuitySignals.loopDetected > 0 ? 'warning' : 'default'}
                />
                <Chip size="small" label={`Repreguntas bloqueadas: ${metrics.continuitySignals.repromptBlocked}`} />
                <Chip size="small" label={`Wiring contexto: ${metrics.continuitySignals.contextWiring}`} />
                <Chip
                  size="small"
                  label={`Escalaciones: ${metrics.continuitySignals.escalatedHuman}`}
                  color={metrics.continuitySignals.escalatedHuman > 0 ? 'warning' : 'default'}
                />
              </Stack>
              {metrics.continuitySignals.rates && (
                <Typography variant="caption" color="text.secondary">
                  loop/context: {Math.round(metrics.continuitySignals.rates.loopPerContext * 100)}% ·
                  escalation/context: {Math.round(metrics.continuitySignals.rates.escalationPerContext * 100)}%
                </Typography>
              )}
              {metrics.continuitySignals.providerResolutionByRole && (
                <Stack spacing={0.8}>
                  <Typography variant="caption" color="text.secondary">
                    Resolucion de proveedores por rol
                  </Typography>
                  {PROVIDER_ROLES.map(({ key, label }) => {
                    const signal = metrics.continuitySignals?.providerResolutionByRole?.[key];
                    if (!signal) return null;
                    return (
                      <Box key={key} sx={{ p: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                        <Typography variant="caption" fontWeight={700}>
                          {label}
                        </Typography>
                        <Typography variant="caption" display="block" color="text.secondary">
                          Primary: {signal.primary} · Fallback: {signal.fallback} · Failed: {signal.failed}
                        </Typography>
                        {signal.providers.length > 0 && (
                          <Typography variant="caption" color="text.secondary">
                            Providers: {signal.providers.join(', ')}
                          </Typography>
                        )}
                      </Box>
                    );
                  })}
                </Stack>
              )}
            </Stack>
          )}

          <Typography variant="subtitle2" sx={{ mt: 1 }}>
            Salud del wizard outbound
          </Typography>
          {!wizardMetrics ? (
            <Alert severity="info">Metricas del wizard no disponibles.</Alert>
          ) : (
            <Stack spacing={0.8}>
              <Typography variant="caption" color="text.secondary">
                Sesiones: {wizardMetrics.funnel.sessionsCreated} · Completadas: {wizardMetrics.funnel.sessionsCompleted}
                · Materializadas: {wizardMetrics.funnel.sessionsMaterialized}
              </Typography>
              <Typography variant="caption">
                Completion rate: <strong>{Math.round(completionRate * 100)}%</strong> · Materialization rate:{' '}
                <strong>{Math.round(materializationRate * 100)}%</strong>
              </Typography>
              {wizardMetrics.dropoff && (
                <Typography variant="caption" color="text.secondary">
                  Dropoff por etapa: language {wizardMetrics.dropoff.language} · task {wizardMetrics.dropoff.task} ·
                  audience {wizardMetrics.dropoff.audience} · tone {wizardMetrics.dropoff.tone}
                </Typography>
              )}
              {completionAlert && (
                <Alert severity="warning">Completion rate bajo (&lt;60%). Revisar friccion en etapas del wizard.</Alert>
              )}
              {materializationAlert && (
                <Alert severity="warning">Materialization rate bajo (&lt;40%). Revisar cierre y defaults.</Alert>
              )}
            </Stack>
          )}

          <Typography variant="subtitle2" sx={{ mt: 1 }}>
            Actividades principales
          </Typography>
          <Stack spacing={0.8}>
            {(metrics.activityMetrics ?? []).slice(0, 5).map((a) => (
              <Box key={a.activityType} sx={{ p: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                <Typography variant="caption" fontWeight={700}>
                  {activityTypeLabel(a.activityType)}
                </Typography>
                <Typography variant="caption" display="block" color="text.secondary">
                  {a.succeeded}/{a.total} exito - {a.avgLatencyMs} ms promedio
                </Typography>
              </Box>
            ))}
          </Stack>

          <Typography variant="subtitle2" sx={{ mt: 1 }}>
            Auditoria reciente
          </Typography>
          <Stack spacing={0.8}>
            {auditEvents.slice(0, 5).map((event) => (
              <Box key={event.id} sx={{ p: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                <Typography variant="caption" fontWeight={700}>
                  {event.actor}
                </Typography>
                <Typography variant="caption" display="block" color="text.secondary">
                  {new Date(event.occurredAt).toLocaleString()}
                </Typography>
              </Box>
            ))}
            {auditEvents.length === 0 && (
              <Typography variant="caption" color="text.secondary">
                Aun no hay eventos de auditoria.
              </Typography>
            )}
          </Stack>
        </Stack>
      )}
    </Card>
  );
}
