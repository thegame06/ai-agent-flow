import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import {
  Box,
  Card,
  Chip,
  Grid,
  Alert,
  Stack,
  Table,
  Button,
  TableRow,
  TableBody,
  TableCell,
  TableHead,
  Typography,
  CardContent,
  CircularProgress,
} from '@mui/material';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

type Bucket = { key: string; count: number };
type AlertSignal = { severity: string; code: string; message: string };
type DeadLetter = {
  id: string;
  agentKey: string;
  reason: string;
  occurredAt: string;
  replayed: boolean;
  replayedAt?: string | null;
  eventId: string;
  eventType: string;
  correlationId?: string | null;
  sessionId?: string | null;
};

type OperationsSummary = {
  tenantId: string;
  inspectedEvents: number;
  totalFallbackOrDenialSignals: number;
  avgEstimatedCostUsd: number;
  p95EstimatedCostUsd: number;
  byPolicy: Bucket[];
  byProvider: Bucket[];
  byModel: Bucket[];
  alerts?: AlertSignal[];
};

function BucketList({ title, items }: { title: string; items: Bucket[] }) {
  return (
    <Card>
      <CardContent>
        <Typography variant="h6" gutterBottom>
          {title}
        </Typography>
        <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
          {items.length === 0 && (
            <Typography variant="body2" color="text.secondary">
              Sin datos.
            </Typography>
          )}
          {items.map((item) => (
            <Chip key={`${title}-${item.key}`} label={`${item.key}: ${item.count}`} variant="outlined" />
          ))}
        </Stack>
      </CardContent>
    </Card>
  );
}

export default function OperationsPage() {
  const tenantId = useTenantId();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<OperationsSummary | null>(null);
  const [deadLetters, setDeadLetters] = useState<DeadLetter[]>([]);
  const [replayBusyId, setReplayBusyId] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    const run = async () => {
      try {
        setLoading(true);
        setError(null);
        const [summaryRes, deadLettersRes] = await Promise.all([
          axios.get(endpoints.agentflow.audit.operationsSummary(tenantId)),
          axios.get(endpoints.agentflow.audit.deadLetters(tenantId)),
        ]);
        if (active) {
          setSummary(summaryRes.data as OperationsSummary);
          setDeadLetters(deadLettersRes.data as DeadLetter[]);
        }
      } catch (e: any) {
        if (active) {
          setError(e?.message ?? 'No se pudo cargar el resumen operativo.');
        }
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    };

    void run();

    return () => {
      active = false;
    };
  }, [tenantId]);

  const replayDeadLetter = async (deadLetterId: string) => {
    try {
      setReplayBusyId(deadLetterId);
      await axios.post(endpoints.agentflow.audit.replayDeadLetter(tenantId, deadLetterId));
      const deadLettersRes = await axios.get(endpoints.agentflow.audit.deadLetters(tenantId));
      setDeadLetters(deadLettersRes.data as DeadLetter[]);
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo reprocesar el evento DLQ.');
    } finally {
      setReplayBusyId(null);
    }
  };

  return (
    <>
      <Helmet>
        <title>
          Operaciones IA | {CONFIG.appName}
        </title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack spacing={3}>
          <Box>
            <Typography variant="h4">Operaciones IA</Typography>
            <Typography variant="body2" color="text.secondary">
              Monitoreo de fallback, denegaciones de politica, costo estimado y dead letters.
            </Typography>
          </Box>

          {loading && (
            <Box sx={{ py: 6, display: 'flex', justifyContent: 'center' }}>
              <CircularProgress />
            </Box>
          )}

          {!!error && <Alert severity="error">{error}</Alert>}

          {!loading && !error && summary && (
            <>
              {(summary.alerts ?? []).map((a) => (
                <Alert key={a.code} severity={a.severity === 'critical' ? 'error' : 'warning'}>
                  {a.message}
                </Alert>
              ))}

              <Grid container spacing={2}>
                <Grid item xs={12} md={3}><Card><CardContent><Typography variant="overline">Eventos inspeccionados</Typography><Typography variant="h4">{summary.inspectedEvents}</Typography></CardContent></Card></Grid>
                <Grid item xs={12} md={3}><Card><CardContent><Typography variant="overline">Senales fallback/deny</Typography><Typography variant="h4">{summary.totalFallbackOrDenialSignals}</Typography></CardContent></Card></Grid>
                <Grid item xs={12} md={3}><Card><CardContent><Typography variant="overline">Costo promedio (USD)</Typography><Typography variant="h4">{summary.avgEstimatedCostUsd.toFixed(4)}</Typography></CardContent></Card></Grid>
                <Grid item xs={12} md={3}><Card><CardContent><Typography variant="overline">Costo P95 (USD)</Typography><Typography variant="h4">{summary.p95EstimatedCostUsd.toFixed(4)}</Typography></CardContent></Card></Grid>
              </Grid>

              <Grid container spacing={2}>
                <Grid item xs={12} md={4}><BucketList title="Fallback/Denegacion por politica" items={summary.byPolicy} /></Grid>
                <Grid item xs={12} md={4}><BucketList title="Senales por provider" items={summary.byProvider} /></Grid>
                <Grid item xs={12} md={4}><BucketList title="Senales por modelo" items={summary.byModel} /></Grid>
              </Grid>

              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>Dead Letter Queue</Typography>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Fecha</TableCell>
                        <TableCell>Agent</TableCell>
                        <TableCell>Evento</TableCell>
                        <TableCell>Razon</TableCell>
                        <TableCell>Estado</TableCell>
                        <TableCell align="right">Accion</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {deadLetters.map((dlq) => (
                        <TableRow key={dlq.id}>
                          <TableCell>{new Date(dlq.occurredAt).toLocaleString()}</TableCell>
                          <TableCell>{dlq.agentKey}</TableCell>
                          <TableCell>{dlq.eventType}</TableCell>
                          <TableCell>{dlq.reason}</TableCell>
                          <TableCell>{dlq.replayed ? 'Replayed' : 'Pending'}</TableCell>
                          <TableCell align="right">
                            <Button
                              size="small"
                              variant="outlined"
                              disabled={!!replayBusyId || dlq.replayed}
                              onClick={() => replayDeadLetter(dlq.id)}
                            >
                              {replayBusyId === dlq.id ? 'Replaying...' : 'Replay'}
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))}
                      {deadLetters.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={6}>Sin eventos en dead letter.</TableCell>
                        </TableRow>
                      )}
                    </TableBody>
                  </Table>
                </CardContent>
              </Card>
            </>
          )}
        </Stack>
      </DashboardContent>
    </>
  );
}
