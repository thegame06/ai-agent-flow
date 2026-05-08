import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import type { WorkflowAuditEvent, WorkflowRuntimeMetrics } from '../types';

type Props = {
  metrics: WorkflowRuntimeMetrics | null;
  auditEvents: WorkflowAuditEvent[];
};

export function RuntimeMetricsCard({ metrics, auditEvents }: Props) {
  return (
    <Card sx={{ p: 2 }}>
      <Typography variant="h6" sx={{ mb: 2 }}>
        Runtime Metrics
      </Typography>
      {!metrics ? (
        <Alert severity="info">Metrics unavailable.</Alert>
      ) : (
        <Stack spacing={1.2}>
          <Typography variant="body2">
            Total: <strong>{metrics.total ?? 0}</strong>
          </Typography>
          <Typography variant="body2">
            Success Rate: <strong>{Math.round((metrics.successRate ?? 0) * 100)}%</strong>
          </Typography>
          <Typography variant="body2">
            Failure Rate: <strong>{Math.round((metrics.failureRate ?? 0) * 100)}%</strong>
          </Typography>
          <Typography variant="body2">
            Avg Latency: <strong>{metrics.avgLatencyMs ?? 0} ms</strong>
          </Typography>
          <Typography variant="subtitle2" sx={{ mt: 1 }}>
            Top Activities
          </Typography>
          <Stack spacing={0.8}>
            {(metrics.activityMetrics ?? []).slice(0, 5).map((a) => (
              <Box key={a.activityType} sx={{ p: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                <Typography variant="caption" fontWeight={700}>
                  {a.activityType}
                </Typography>
                <Typography variant="caption" display="block" color="text.secondary">
                  {a.succeeded}/{a.total} success - {a.avgLatencyMs} ms avg
                </Typography>
              </Box>
            ))}
          </Stack>
          <Typography variant="subtitle2" sx={{ mt: 1 }}>
            Recent Audit
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
                No audit events yet.
              </Typography>
            )}
          </Stack>
        </Stack>
      )}
    </Card>
  );
}
