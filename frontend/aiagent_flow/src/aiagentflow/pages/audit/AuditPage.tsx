import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import TableRow from '@mui/material/TableRow';
import MenuItem from '@mui/material/MenuItem';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';
import TableContainer from '@mui/material/TableContainer';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { TermHelp } from 'src/aiagentflow/components/TermHelp';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { useSettingsWorkspace } from 'src/aiagentflow/pages/settings/SettingsWorkspaceContext';

import { Label } from 'src/components/label';

type AuditLogEntry = {
  id: string;
  occurredAt: string;
  actor: string;
  action: string;
  resource: string;
  severity: string;
  correlationId: string;
  executionId: string;
  eventJson: string;
};

type JourneyResponse = {
  summary: {
    correlationId: string;
    startedAt?: string;
    lastUpdatedAt?: string;
    currentStage: string;
    customerBecameClient: boolean;
    sessionStatus: string;
    channel: string;
    customer: {
      identifier: string;
      displayName?: string;
      kind: string;
    };
    firstCustomerMessage?: string;
    lastVisibleReply?: string;
    agentCount: number;
    workflowCount: number;
    toolCount: number;
    messageCount: number;
    salesCount: number;
    invoicesCount: number;
    paidInvoicesCount: number;
    salesTotal: number;
    invoicedTotal: number;
    paidTotal: number;
  };
  crossCutting: {
    session?: {
      sessionId: string;
      threadId?: string;
      channelId: string;
      channelType: string;
      status: string;
      windowOpen: boolean;
      createdAt: string;
      lastActivityAt: string;
      expiresAt?: string;
    };
    thread?: {
      threadId: string;
      status: string;
      turnCount: number;
      createdAt: string;
      lastActivityAt?: string;
    };
    agents: Array<{
      agentId: string;
      agentName: string;
      executionCount: number;
      statuses: string[];
      roles: string[];
    }>;
    tools: Array<{
      toolName: string;
      invocations: number;
      successCount: number;
      failureCount: number;
      firstUsedAt: string;
      lastUsedAt: string;
    }>;
    workflows: Array<{
      workflowId: string;
      action: string;
      occurredAt: string;
    }>;
    decisions: Array<{
      kind: string;
      title: string;
      explanation: string;
      source: string;
      occurredAt: string;
    }>;
  };
  timeline: Array<{
    id: string;
    occurredAt: string;
    category: string;
    title: string;
    description: string;
    detail?: string;
  }>;
};

const severityColor = (severity: string) => {
  switch (severity) {
    case 'critical':
    case 'error':
      return 'error';
    case 'warning':
      return 'warning';
    case 'success':
      return 'success';
    default:
      return 'info';
  }
};

const stageColor = (stage: string) => {
  switch (stage) {
    case 'paid':
      return 'success';
    case 'invoiced':
      return 'secondary';
    case 'sale_created':
      return 'warning';
    case 'customer':
      return 'info';
    default:
      return 'default';
  }
};

const categoryLabel: Record<string, string> = {
  session: 'Sesion',
  customer_message: 'Mensaje del cliente',
  reply: 'Respuesta',
  routing: 'Decision',
  agent_execution: 'Agente',
  tool_usage: 'Herramientas',
  workflow: 'Flujo',
  handoff: 'Transferencia',
  commerce: 'Conversion',
  error: 'Error',
  security: 'Seguridad',
};

const stageLabel: Record<string, string> = {
  lead: 'Lead',
  customer: 'Cliente',
  sale_created: 'Venta creada',
  invoiced: 'Facturado',
  paid: 'Cobrado',
};

const sessionStatusLabel = (value?: string) => {
  if (!value) return 'Sin estado';

  const labels: Record<string, string> = {
    Open: 'Activa',
    Active: 'Activa',
    Closed: 'Cerrada',
    Expired: 'Vencida',
  };

  return labels[value] ?? value;
};

const toolNameLabel = (value: string) => value.replace(/[._]/g, ' ');

export default function AuditPage() {
  const { embedded } = useSettingsWorkspace();
  const theme = useTheme();
  const tenantId = useTenantId();
  const [logs, setLogs] = useState<AuditLogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [journeyLoading, setJourneyLoading] = useState(false);
  const [error, setError] = useState('');
  const [correlationId, setCorrelationId] = useState('');
  const [action, setAction] = useState('');
  const [limit, setLimit] = useState(150);
  const [fromAt, setFromAt] = useState('');
  const [toAt, setToAt] = useState('');
  const [runtimeFilter, setRuntimeFilter] = useState<'all' | 'Text' | 'Voice' | 'MultimodalRealtime'>('all');
  const [modelRoleFilter, setModelRoleFilter] = useState<'all' | 'brain' | 'stt' | 'tts'>('all');
  const [correlations, setCorrelations] = useState<any[]>([]);
  const [journey, setJourney] = useState<JourneyResponse | null>(null);

  const parseEventJson = (raw: string) => {
    try {
      return JSON.parse(raw || '{}') as Record<string, any>;
    } catch {
      return {} as Record<string, any>;
    }
  };

  const filteredLogs = useMemo(
    () =>
      logs.filter((entry) => {
        const payload = parseEventJson(entry.eventJson);

        if (runtimeFilter !== 'all') {
          const eventRuntime =
            payload?.runtimeKind
            ?? payload?.metadata?.runtimeKind
            ?? payload?.context?.runtimeKind
            ?? '';

          if (String(eventRuntime).toLowerCase() !== runtimeFilter.toLowerCase()) {
            return false;
          }
        }

        if (modelRoleFilter !== 'all') {
          const asText = JSON.stringify(payload).toLowerCase();

          if (modelRoleFilter === 'brain') return asText.includes('reasoning') || asText.includes('brain');
          if (modelRoleFilter === 'stt') return asText.includes('stt') || asText.includes('speech');
          if (modelRoleFilter === 'tts') return asText.includes('tts') || asText.includes('synth');
        }

        return true;
      }),
    [logs, runtimeFilter, modelRoleFilter]
  );

  const fetchJourney = useCallback(async (targetCorrelationId: string) => {
    if (!targetCorrelationId.trim()) {
      setJourney(null);
      return;
    }

    try {
      setJourneyLoading(true);
      const response = await axios.get(endpoints.agentflow.audit.journey(tenantId, targetCorrelationId.trim()));
      setJourney(response.data);
    } catch (e: any) {
      setJourney(null);
      setError(e?.message ?? 'No se pudo construir la historia del caso.');
    } finally {
      setJourneyLoading(false);
    }
  }, [tenantId]);

  const fetchLogs = useCallback(async (targetCorrelationId?: string) => {
    try {
      setLoading(true);
      setError('');
      const activeCorrelation = (targetCorrelationId ?? correlationId).trim();
      const params = new URLSearchParams();
      params.set('limit', String(limit));
      if (activeCorrelation) params.set('correlationId', activeCorrelation);
      if (action.trim()) params.set('action', action.trim());
      if (fromAt) params.set('from', new Date(fromAt).toISOString());
      if (toAt) params.set('to', new Date(toAt).toISOString());

      const [response, corrResponse] = await Promise.all([
        axios.get(`${endpoints.agentflow.audit.list(tenantId)}?${params.toString()}`),
        axios.get(`${endpoints.agentflow.audit.correlations(tenantId)}?limit=30`),
      ]);

      setLogs(response.data ?? []);
      setCorrelations(corrResponse.data ?? []);

      if (activeCorrelation) {
        await fetchJourney(activeCorrelation);
      } else {
        setJourney(null);
      }
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo cargar la auditoria.');
    } finally {
      setLoading(false);
    }
  }, [tenantId, limit, correlationId, action, fromAt, toAt, fetchJourney]);

  const downloadAudit = useCallback(async (format: 'csv' | 'json') => {
    try {
      const activeCorrelation = correlationId.trim();
      const params = new URLSearchParams();
      params.set('limit', String(limit));
      params.set('format', format);
      if (activeCorrelation) params.set('correlationId', activeCorrelation);
      if (action.trim()) params.set('action', action.trim());
      if (fromAt) params.set('from', new Date(fromAt).toISOString());
      if (toAt) params.set('to', new Date(toAt).toISOString());

      const response = await axios.get(`${endpoints.agentflow.audit.list(tenantId)}/export?${params.toString()}`, {
        responseType: 'blob',
      });

      const blob = new Blob([response.data], { type: format === 'csv' ? 'text/csv;charset=utf-8;' : 'application/json' });
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `audit-${tenantId}.${format}`);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo descargar el archivo de auditoria.');
    }
  }, [tenantId, correlationId, limit, action, fromAt, toAt]);

  useEffect(() => {
    void fetchLogs();
  }, [fetchLogs]);

  const issueCount = useMemo(
    () => filteredLogs.filter((e) => e.severity === 'critical' || e.severity === 'error').length,
    [filteredLogs]
  );

  const warningsCount = useMemo(
    () => filteredLogs.filter((e) => e.severity === 'warning').length,
    [filteredLogs]
  );

  const summary = journey?.summary;

  return (
    <>
      <Helmet>
        <title>Auditoria | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl" disablePadding={embedded}>
        <Box sx={{ mb: 4 }}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography variant="h4">Auditoria explicada</Typography>
            <TermHelp title="Aqui ves la historia completa de un caso: que escribio el cliente, como respondio el sistema, que decisiones tomo y en que termino comercialmente." />
          </Stack>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Sigue el caso completo desde el primer mensaje del lead hasta el resultado comercial, sin exigir que el usuario entienda reglas internas, motivos detectados, flujos automatizados o herramientas tecnicas.
          </Typography>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
          <TextField
            label="ID de seguimiento del caso"
            value={correlationId}
            onChange={(e) => setCorrelationId(e.target.value)}
            helperText="Puedes pegar el ID de la conversacion o elegir un caso reciente abajo."
            fullWidth
          />
          <TextField
            label="Tipo de evento"
            value={action}
            onChange={(e) => setAction(e.target.value)}
            placeholder="Decision, Transferencia, Venta creada..."
            fullWidth
          />
          <TextField
            label="Maximo"
            type="number"
            value={limit}
            onChange={(e) => setLimit(Number(e.target.value || 100))}
            sx={{ minWidth: 120 }}
          />
          <TextField
            label="Desde"
            type="datetime-local"
            value={fromAt}
            onChange={(e) => setFromAt(e.target.value)}
            InputLabelProps={{ shrink: true }}
            sx={{ minWidth: 220 }}
          />
          <TextField
            label="Hasta"
            type="datetime-local"
            value={toAt}
            onChange={(e) => setToAt(e.target.value)}
            InputLabelProps={{ shrink: true }}
            sx={{ minWidth: 220 }}
          />
          <TextField
            label="Runtime"
            select
            value={runtimeFilter}
            onChange={(e) => setRuntimeFilter(e.target.value as any)}
            sx={{ minWidth: 180 }}
          >
            <MenuItem value="all">Todos</MenuItem>
            <MenuItem value="Text">Text</MenuItem>
            <MenuItem value="Voice">Voice</MenuItem>
            <MenuItem value="MultimodalRealtime">MultimodalRealtime</MenuItem>
          </TextField>
          <TextField
            label="Rol modelo"
            select
            value={modelRoleFilter}
            onChange={(e) => setModelRoleFilter(e.target.value as any)}
            sx={{ minWidth: 170 }}
          >
            <MenuItem value="all">Todos</MenuItem>
            <MenuItem value="brain">Brain</MenuItem>
            <MenuItem value="stt">STT</MenuItem>
            <MenuItem value="tts">TTS</MenuItem>
          </TextField>
          <Button variant="contained" onClick={() => void fetchLogs()}>
            Aplicar
          </Button>
          <Button variant="outlined" onClick={() => void downloadAudit('csv')}>
            Descargar CSV
          </Button>
          <Button variant="outlined" onClick={() => void downloadAudit('json')}>
            Descargar JSON
          </Button>
        </Stack>

        <Stack direction="row" spacing={2} sx={{ mb: 3, flexWrap: 'wrap' }} useFlexGap>
          <Chip label={`${filteredLogs.length} eventos`} color="primary" variant="soft" />
          <Chip label={`${issueCount} errores`} color="error" variant="soft" />
          <Chip label={`${warningsCount} alertas`} color="warning" variant="soft" />
        </Stack>

        <Card sx={{ mb: 3, p: 2, border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}` }}>
          <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Casos recientes</Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {correlations.length === 0 ? (
              <Chip size="small" label="Aun no hay casos recientes" />
            ) : (
              correlations.map((item) => (
                <Button
                  key={item.correlationId}
                  size="small"
                  variant={correlationId === item.correlationId ? 'contained' : 'outlined'}
                  onClick={() => {
                    setCorrelationId(item.correlationId);
                    void fetchLogs(item.correlationId);
                  }}
                >
                  {item.correlationId} ({item.eventCount})
                </Button>
              ))
            )}
          </Stack>
        </Card>

        {(journeyLoading || summary) && (
          <Card sx={{ mb: 3, overflow: 'hidden', border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}` }}>
            {journeyLoading && <LinearProgress />}
            {summary && (
              <Box sx={{ p: 2.5 }}>
                <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} sx={{ mb: 2 }}>
                  <Box>
                    <Typography variant="h5">Historia del caso</Typography>
                    <Typography variant="body2" color="text.secondary">
                      ID de seguimiento: {summary.correlationId}
                    </Typography>
                  </Box>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    <Chip label={stageLabel[summary.currentStage] ?? summary.currentStage} color={stageColor(summary.currentStage) as any} />
                    <Chip label={summary.customerBecameClient ? 'Convertido a cliente' : 'Aun es lead'} color={summary.customerBecameClient ? 'success' : 'default'} />
                    <Chip label={sessionStatusLabel(summary.sessionStatus)} variant="outlined" />
                  </Stack>
                </Stack>

                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="overline" color="text.secondary">Cliente</Typography>
                    <Typography variant="subtitle1">
                      {summary.customer.displayName || summary.customer.identifier || 'Sin nombre'}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      Tipo: {summary.customer.kind} | Canal: {summary.channel}
                    </Typography>
                    <Typography variant="body2" sx={{ mt: 1 }}>
                      Primer mensaje:
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {summary.firstCustomerMessage || 'No hay mensaje visible.'}
                    </Typography>
                  </Card>

                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="overline" color="text.secondary">Proceso</Typography>
                    <Typography variant="subtitle1">
                      {summary.agentCount} asistentes, {summary.workflowCount} flujos, {summary.toolCount} herramientas
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {summary.messageCount} mensajes visibles en la historia del caso.
                    </Typography>
                    <Typography variant="body2" sx={{ mt: 1 }}>
                      Ultima respuesta visible:
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {summary.lastVisibleReply || 'No se encontro respuesta final visible.'}
                    </Typography>
                  </Card>

                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="overline" color="text.secondary">Resultado</Typography>
                    <Typography variant="subtitle1">
                      Ventas: {summary.salesCount} | Facturas: {summary.invoicesCount} | Pagadas: {summary.paidInvoicesCount}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      Monto vendido: ${Number(summary.salesTotal || 0).toFixed(2)}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      Monto facturado: ${Number(summary.invoicedTotal || 0).toFixed(2)}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      Monto cobrado: ${Number(summary.paidTotal || 0).toFixed(2)}
                    </Typography>
                  </Card>
                </Stack>

                <Divider sx={{ my: 2 }} />

                <Typography variant="subtitle1" sx={{ mb: 1.5 }}>Ficha transversal del caso</Typography>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="subtitle2" sx={{ mb: 1 }}>Conversacion</Typography>
                    <Stack spacing={0.75}>
                      <Typography variant="body2" color="text.secondary">
                        Canal: {journey.crossCutting.session?.channelType ?? summary.channel}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Estado: {sessionStatusLabel(journey.crossCutting.session?.status ?? summary.sessionStatus)}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Ventana abierta: {journey.crossCutting.session?.windowOpen ? 'Si' : 'No'}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Inicio: {journey.crossCutting.session?.createdAt ? new Date(journey.crossCutting.session.createdAt).toLocaleString() : 'No disponible'}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Ultima actividad: {journey.crossCutting.session?.lastActivityAt ? new Date(journey.crossCutting.session.lastActivityAt).toLocaleString() : 'No disponible'}
                      </Typography>
                    </Stack>
                  </Card>

                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="subtitle2" sx={{ mb: 1 }}>Seguimiento interno</Typography>
                    <Stack spacing={0.75}>
                      <Typography variant="body2" color="text.secondary">
                        ID de conversacion: {journey.crossCutting.session?.sessionId ?? 'No disponible'}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Hilo de trabajo: {journey.crossCutting.thread?.threadId ?? journey.crossCutting.session?.threadId ?? 'No disponible'}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Turnos registrados: {journey.crossCutting.thread?.turnCount ?? 0}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        Estado del hilo: {journey.crossCutting.thread?.status ?? 'No disponible'}
                      </Typography>
                    </Stack>
                  </Card>

                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="subtitle2" sx={{ mb: 1 }}>Flujos detectados</Typography>
                    <Stack spacing={0.75}>
                      {journey.crossCutting.workflows.length === 0 ? (
                        <Typography variant="body2" color="text.secondary">
                          No se registraron flujos automatizados para este caso.
                        </Typography>
                      ) : (
                        journey.crossCutting.workflows.slice(0, 5).map((item) => (
                          <Typography key={`${item.workflowId}-${item.occurredAt}`} variant="body2" color="text.secondary">
                            {item.action || 'Ejecucion'}: {item.workflowId} ({new Date(item.occurredAt).toLocaleString()})
                          </Typography>
                        ))
                      )}
                    </Stack>
                  </Card>
                </Stack>

                <Typography variant="subtitle1" sx={{ mb: 1.5 }}>Timeline entendible</Typography>
                <Stack spacing={1.25}>
                  {journey.timeline.map((item, index) => (
                    <Card key={item.id} variant="outlined" sx={{ p: 1.5 }}>
                      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={1} sx={{ mb: 0.75 }}>
                        <Stack direction="row" spacing={1} alignItems="center">
                          <Chip size="small" label={`${index + 1}`} />
                          <Chip size="small" variant="outlined" label={categoryLabel[item.category] ?? item.category} />
                          <Typography variant="subtitle2">{item.title}</Typography>
                        </Stack>
                        <Typography variant="caption" color="text.secondary">
                          {new Date(item.occurredAt).toLocaleString()}
                        </Typography>
                      </Stack>
                      <Typography variant="body2">{item.description}</Typography>
                      {item.detail && (
                        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
                          {item.detail}
                        </Typography>
                      )}
                    </Card>
                  ))}
                </Stack>

                <Divider sx={{ my: 2 }} />

                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="subtitle2" sx={{ mb: 1 }}>Decisiones clave</Typography>
                    <Stack spacing={1}>
                      {journey.crossCutting.decisions.length === 0 && (
                        <Typography variant="body2" color="text.secondary">No se registraron decisiones relevantes.</Typography>
                      )}
                      {journey.crossCutting.decisions.slice(0, 6).map((item) => (
                        <Box key={`${item.kind}-${item.occurredAt}`}>
                          <Typography variant="body2" fontWeight={600}>{item.title}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {item.explanation} Fuente: {item.source}. {new Date(item.occurredAt).toLocaleString()}
                          </Typography>
                        </Box>
                      ))}
                    </Stack>
                  </Card>

                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="subtitle2" sx={{ mb: 1 }}>Agentes involucrados</Typography>
                    <Stack spacing={1}>
                      {journey.crossCutting.agents.length === 0 && (
                        <Typography variant="body2" color="text.secondary">No se encontraron ejecuciones asociadas.</Typography>
                      )}
                      {journey.crossCutting.agents.map((item) => (
                        <Box key={item.agentId}>
                          <Typography variant="body2" fontWeight={600}>{item.agentName}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {item.executionCount} ejecuciones | Rol: {item.roles.join(', ') || 'n/a'} | Estados: {item.statuses.join(', ')}
                          </Typography>
                        </Box>
                      ))}
                    </Stack>
                  </Card>

                  <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
                    <Typography variant="subtitle2" sx={{ mb: 1 }}>Herramientas usadas</Typography>
                    <Stack spacing={1}>
                      {journey.crossCutting.tools.length === 0 && (
                        <Typography variant="body2" color="text.secondary">No se detectaron herramientas usadas dentro de las ejecuciones.</Typography>
                      )}
                      {journey.crossCutting.tools.slice(0, 8).map((item) => (
                        <Box key={item.toolName}>
                          <Typography variant="body2" fontWeight={600}>{toolNameLabel(item.toolName)}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {item.invocations} usos | exitosas {item.successCount} | con falla {item.failureCount}
                          </Typography>
                        </Box>
                      ))}
                    </Stack>
                  </Card>
                </Stack>
              </Box>
            )}
          </Card>
        )}

        <Card sx={{ overflow: 'hidden', border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}` }}>
          {loading ? (
            <LinearProgress />
          ) : (
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ fontWeight: 700 }}>Fecha y hora</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Actor</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Evento</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Recurso</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>ID de seguimiento</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Severidad</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Detalle tecnico</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {filteredLogs.map((entry) => (
                    <TableRow key={entry.id} hover>
                      <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.8rem' }}>
                        {new Date(entry.occurredAt).toLocaleString()}
                      </TableCell>
                      <TableCell>{entry.actor || 'Sistema'}</TableCell>
                      <TableCell><Chip label={entry.action} size="small" variant="outlined" /></TableCell>
                      <TableCell sx={{ maxWidth: 250, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {entry.resource || '-'}
                      </TableCell>
                      <TableCell sx={{ maxWidth: 260, overflow: 'hidden', textOverflow: 'ellipsis', fontFamily: 'monospace', fontSize: '0.75rem' }}>
                        {entry.correlationId || '-'}
                      </TableCell>
                      <TableCell><Label color={severityColor(entry.severity)}>{entry.severity}</Label></TableCell>
                      <TableCell sx={{ maxWidth: 340, overflow: 'hidden', textOverflow: 'ellipsis', fontFamily: 'monospace', fontSize: '0.75rem' }}>
                        {entry.eventJson || '-'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </Card>
      </DashboardContent>
    </>
  );
}
