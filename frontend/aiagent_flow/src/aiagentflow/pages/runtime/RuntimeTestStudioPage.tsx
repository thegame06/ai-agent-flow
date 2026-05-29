import { useParams } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Select from '@mui/material/Select';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardHeader from '@mui/material/CardHeader';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

type RuntimeRouteKind = 'text' | 'voice' | 'multimodal';

type TimelineEvent = {
  timestamp: string;
  stage: string;
  direction: string;
  payloadType: string;
  status: string;
  errorCode?: string;
  message?: string;
  correlationId: string;
};

type SessionSummary = {
  testSessionId: string;
  runtimeKind: string;
  status: string;
  correlationId: string;
  mode: string;
  agentId?: string;
  channelId?: string;
  threadId?: string;
  createdAt: string;
  updatedAt: string;
};

type AgentOption = {
  id: string;
  name?: string;
  status?: string;
};

type ChannelOption = {
  id: string;
  name?: string;
  type?: string;
  status?: string;
};

const runtimeMap: Record<RuntimeRouteKind, { apiLabel: string; uiLabel: string }> = {
  text: { apiLabel: 'Text', uiLabel: 'Texto' },
  voice: { apiLabel: 'Voice', uiLabel: 'Voz' },
  multimodal: { apiLabel: 'MultimodalRealtime', uiLabel: 'Multimodal' },
};

export default function RuntimeTestStudioPage() {
  const tenantId = useTenantId();
  const { runtimeKind = 'text' } = useParams();
  const routeRuntime = ((runtimeKind as RuntimeRouteKind) || 'text') as RuntimeRouteKind;
  const runtimeConfig = runtimeMap[routeRuntime] ?? runtimeMap.text;
  const runtimeLabel = runtimeConfig.apiLabel;

  const [mode, setMode] = useState<'direct' | 'thread' | 'channel'>('direct');
  const [agentId, setAgentId] = useState('');
  const [channelId, setChannelId] = useState('');
  const [sessionId, setSessionId] = useState('');
  const [correlationId, setCorrelationId] = useState('');
  const [content, setContent] = useState('');
  const [attachmentName, setAttachmentName] = useState('');
  const [attachmentType, setAttachmentType] = useState('image/png');
  const [attachmentSize, setAttachmentSize] = useState('1024');
  const [attachmentRefs, setAttachmentRefs] = useState<string[]>([]);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [timeline, setTimeline] = useState<TimelineEvent[]>([]);
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [agents, setAgents] = useState<AgentOption[]>([]);
  const [channels, setChannels] = useState<ChannelOption[]>([]);
  const [timelineStageFilter, setTimelineStageFilter] = useState('all');
  const [timelineStatusFilter, setTimelineStatusFilter] = useState('all');
  const [transcript, setTranscript] = useState<any>(null);
  const [metrics, setMetrics] = useState<any>(null);
  const [response, setResponse] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mapError = (raw: string | null) => {
    if (!raw) return null;
    if (raw.includes('session_rate_limited')) return 'Se alcanzo el limite de sesiones. Espera unos segundos e intenta de nuevo.';
    if (raw.includes('runtime_incompatible')) return 'La modalidad seleccionada no es compatible con esta operacion.';
    if (raw.includes('attachment_not_supported')) return 'El adjunto no esta soportado para este flujo.';
    if (raw.includes('agent_required')) return 'Debes seleccionar un asistente para este modo.';
    return raw;
  };

  useEffect(() => {
    const loadOptions = async () => {
      try {
        const [agentsRes, channelsRes] = await Promise.all([
          axios.get(endpoints.agentflow.agents.list(tenantId)),
          axios.get(endpoints.agentflow.channels.list(tenantId)),
        ]);

        setAgents(
          ((agentsRes.data ?? []) as AgentOption[]).filter(
            (agent) => agent?.id && agent.status !== 'Archived'
          )
        );
        setChannels((channelsRes.data ?? []) as ChannelOption[]);
      } catch {
        setAgents([]);
        setChannels([]);
      }
    };

    void loadOptions();
  }, [tenantId]);

  const limitations = useMemo(() => {
    if (routeRuntime === 'text') {
      return 'La modalidad texto admite pruebas directas, por hilo y por canal. Los adjuntos se registran como referencias de metadata.';
    }
    if (routeRuntime === 'voice') {
      return 'La modalidad voz se valida en esta fase por integracion y callbacks. Aun no incluye softphone ni WebRTC en navegador.';
    }
    return 'La modalidad multimodal valida contrato, adjuntos y trazabilidad. El transporte bidireccional completo aun esta pendiente.';
  }, [routeRuntime]);

  const refreshTimeline = useCallback(
    async (activeSessionId: string) => {
      const res = await axios.get(
        endpoints.agentflow.testStudio.timeline(tenantId, runtimeLabel, activeSessionId)
      );
      setTimeline(res.data?.timelineEvents ?? []);
    },
    [tenantId, runtimeLabel]
  );

  useEffect(() => {
    if (!sessionId || routeRuntime !== 'voice') return undefined;
    const handle = window.setInterval(() => {
      refreshTimeline(sessionId).catch(() => undefined);
    }, 3000);
    return () => window.clearInterval(handle);
  }, [sessionId, routeRuntime, refreshTimeline]);

  const loadSessions = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(endpoints.agentflow.testStudio.listSessions(tenantId, runtimeLabel));
      setSessions(res.data ?? []);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudieron cargar las sesiones.');
    } finally {
      setLoading(false);
    }
  };

  const createSession = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(
        endpoints.agentflow.testStudio.createSession(tenantId, runtimeLabel),
        {
          mode,
          agentId: agentId || undefined,
          channelId: channelId || undefined,
          channelType: mode === 'channel' ? 'Api' : undefined,
          correlationId: correlationId || undefined,
        }
      );
      const newSessionId = res.data?.testSessionId as string;
      setSessionId(newSessionId);
      if (res.data?.correlationId) setCorrelationId(res.data.correlationId);
      await refreshTimeline(newSessionId);
      setResponse(res.data);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo crear la sesion de prueba.');
    } finally {
      setLoading(false);
    }
  };

  const registerAttachment = async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(
        endpoints.agentflow.testStudio.registerAttachment(tenantId, runtimeLabel, sessionId),
        {
          name: attachmentName || 'attachment',
          contentType: attachmentType,
          sizeBytes: Number(attachmentSize || 0),
        }
      );
      const nextRefs = [...attachmentRefs, res.data?.attachmentRef].filter(Boolean);
      setAttachmentRefs(nextRefs);
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo registrar el adjunto.');
    } finally {
      setLoading(false);
    }
  };

  const uploadAttachment = async () => {
    if (!sessionId || !selectedFile) return;
    setLoading(true);
    setError(null);
    try {
      const formData = new FormData();
      formData.append('file', selectedFile);
      const res = await axios.post(
        endpoints.agentflow.testStudio.uploadAttachment(tenantId, runtimeLabel, sessionId),
        formData,
        {
          headers: { 'Content-Type': 'multipart/form-data' },
        }
      );
      const nextRefs = [...attachmentRefs, res.data?.attachmentRef].filter(Boolean);
      setAttachmentRefs(nextRefs);
      setSelectedFile(null);
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo subir el archivo.');
    } finally {
      setLoading(false);
    }
  };

  const sendMessage = async () => {
    if (!sessionId || !content.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(
        endpoints.agentflow.testStudio.sendMessage(tenantId, runtimeLabel, sessionId),
        {
          content: content.trim(),
          attachmentRefs,
        }
      );
      setResponse(res.data);
      setContent('');
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo enviar el mensaje.');
    } finally {
      setLoading(false);
    }
  };

  const closeSession = async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      await axios.post(endpoints.agentflow.testStudio.close(tenantId, runtimeLabel, sessionId), {});
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo cerrar la sesion.');
    } finally {
      setLoading(false);
    }
  };

  const loadTranscript = async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(
        endpoints.agentflow.testStudio.transcript(tenantId, runtimeLabel, sessionId)
      );
      setTranscript(res.data);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo cargar la transcripcion.');
    } finally {
      setLoading(false);
    }
  };

  const loadMetrics = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(endpoints.agentflow.testStudio.metrics(tenantId, runtimeLabel));
      setMetrics(res.data);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudieron cargar las metricas.');
    } finally {
      setLoading(false);
    }
  };

  const linkCorrelation = async () => {
    if (!sessionId || !correlationId.trim()) return;
    setLoading(true);
    setError(null);
    try {
      await axios.patch(
        endpoints.agentflow.testStudio.updateCorrelation(tenantId, runtimeLabel, sessionId),
        { correlationId: correlationId.trim() }
      );
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'No se pudo vincular el identificador externo.');
    } finally {
      setLoading(false);
    }
  };

  const filteredTimeline = timeline.filter((event) => {
    if (timelineStageFilter !== 'all' && event.stage !== timelineStageFilter) return false;
    if (timelineStatusFilter !== 'all' && event.status !== timelineStatusFilter) return false;
    return true;
  });

  const modeLabels: Record<'direct' | 'thread' | 'channel', string> = {
    direct: 'Directo',
    thread: 'Por hilo',
    channel: 'Por canal',
  };

  return (
    <>
      <Helmet>
        <title>Centro de pruebas | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent>
        <Stack spacing={2}>
          <Typography variant="h4">Centro de pruebas � {runtimeConfig.uiLabel}</Typography>
          <Alert severity="info">{limitations}</Alert>
          {error && <Alert severity="error">{error}</Alert>}
          <Grid container spacing={2}>
            <Grid item xs={12} md={6}>
              <Card>
                <CardHeader title="Sesion" subheader="Crea o retoma una sesion de prueba" />
                <CardContent>
                  <Stack spacing={2}>
                    <TextField
                      select
                      label="Modo"
                      value={mode}
                      onChange={(e) => setMode(e.target.value as 'direct' | 'thread' | 'channel')}
                    >
                      <MenuItem value="direct">Directo</MenuItem>
                      <MenuItem value="thread">Por hilo</MenuItem>
                      <MenuItem value="channel">Por canal</MenuItem>
                    </TextField>
                    <TextField
                      select
                      label="Asistente"
                      value={agentId}
                      onChange={(e) => setAgentId(e.target.value)}
                      helperText="Selecciona un asistente publicado para pruebas directas o por hilo."
                    >
                      <MenuItem value="">Sin asistente</MenuItem>
                      {agents.map((agent) => (
                        <MenuItem key={agent.id} value={agent.id}>
                          {agent.name || agent.id}
                        </MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      select
                      label="Canal"
                      value={channelId}
                      onChange={(e) => setChannelId(e.target.value)}
                      helperText="Solo aplica cuando el modo de sesion es por canal."
                    >
                      <MenuItem value="">Sin canal</MenuItem>
                      {channels.map((channel) => (
                        <MenuItem key={channel.id} value={channel.id}>
                          {(channel.name || channel.id) + (channel.type ? ` � ${channel.type}` : '')}
                        </MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      label="Identificador externo"
                      value={correlationId}
                      onChange={(e) => setCorrelationId(e.target.value)}
                      helperText="Usalo para vincular eventos externos como CallSid en voz."
                    />
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                      <Button variant="contained" onClick={createSession} disabled={loading}>
                        Crear sesion
                      </Button>
                      <Button variant="outlined" onClick={loadSessions} disabled={loading}>
                        Cargar sesiones
                      </Button>
                      <Button
                        variant="outlined"
                        onClick={linkCorrelation}
                        disabled={loading || !sessionId || !correlationId.trim()}
                      >
                        Vincular identificador
                      </Button>
                    </Stack>
                    <TextField
                      label="ID de sesion"
                      value={sessionId}
                      onChange={(e) => setSessionId(e.target.value)}
                    />
                    <TextField
                      select
                      label="Sesiones existentes"
                      value={sessionId}
                      onChange={(e) => setSessionId(e.target.value)}
                    >
                      <MenuItem value="">Seleccionar sesion</MenuItem>
                      {sessions.map((session) => (
                        <MenuItem key={session.testSessionId} value={session.testSessionId}>
                          {`${session.testSessionId} � ${session.status} � ${modeLabels[session.mode as keyof typeof modeLabels] ?? session.mode}`}
                        </MenuItem>
                      ))}
                    </TextField>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            <Grid item xs={12} md={6}>
              <Card>
                <CardHeader title="Mensaje" subheader="Envia entradas y revisa la trazabilidad" />
                <CardContent>
                  <Stack spacing={2}>
                    <TextField
                      label="Mensaje"
                      multiline
                      minRows={3}
                      value={content}
                      onChange={(e) => setContent(e.target.value)}
                    />
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                      <Button variant="contained" onClick={sendMessage} disabled={loading || !sessionId}>
                        Enviar
                      </Button>
                      <Button variant="outlined" onClick={closeSession} disabled={loading || !sessionId}>
                        Cerrar sesion
                      </Button>
                    </Stack>
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                      <Button variant="outlined" onClick={loadTranscript} disabled={loading || !sessionId}>
                        Ver transcripcion
                      </Button>
                      <Button variant="outlined" onClick={loadMetrics} disabled={loading}>
                        Ver metricas
                      </Button>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            <Grid item xs={12} md={6}>
              <Card>
                <CardHeader title="Adjuntos" subheader="Registra referencias o sube archivos de apoyo" />
                <CardContent>
                  <Stack spacing={2}>
                    <TextField label="Nombre" value={attachmentName} onChange={(e) => setAttachmentName(e.target.value)} />
                    <TextField label="Tipo de contenido" value={attachmentType} onChange={(e) => setAttachmentType(e.target.value)} />
                    <TextField label="Tamano en bytes" value={attachmentSize} onChange={(e) => setAttachmentSize(e.target.value)} />
                    <Button variant="outlined" onClick={registerAttachment} disabled={loading || !sessionId}>
                      Registrar adjunto
                    </Button>
                    <Button variant="outlined" component="label" disabled={loading || !sessionId}>
                      Seleccionar archivo
                      <input
                        type="file"
                        hidden
                        onChange={(e) => setSelectedFile(e.target.files?.[0] ?? null)}
                      />
                    </Button>
                    {selectedFile && (
                      <Typography variant="caption">
                        seleccionado: {selectedFile.name} ({selectedFile.type || 'desconocido'}, {selectedFile.size} bytes)
                      </Typography>
                    )}
                    <Button
                      variant="contained"
                      onClick={uploadAttachment}
                      disabled={loading || !sessionId || !selectedFile}
                    >
                      Subir archivo
                    </Button>
                    <Typography variant="body2">
                      Referencias activas: {attachmentRefs.join(', ') || '-'}
                    </Typography>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            <Grid item xs={12} md={6}>
              <Card>
                <CardHeader title="Ultima respuesta" />
                <CardContent>
                  <Box component="pre" sx={{ whiteSpace: 'pre-wrap', m: 0 }}>
                    {JSON.stringify(response, null, 2)}
                  </Box>
                </CardContent>
              </Card>
            </Grid>
            <Grid item xs={12} md={6}>
              <Card>
                <CardHeader title="Transcripcion" />
                <CardContent>
                  {transcript?.entries?.length ? (
                    <Stack spacing={1}>
                      {transcript.entries.map((entry: any, idx: number) => (
                        <Box key={`${entry.timestamp}-${idx}`}>
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Chip size="small" label={entry.speaker} />
                            <Typography variant="caption">
                              {new Date(entry.timestamp).toLocaleString()} � {entry.stage} � {entry.status}
                            </Typography>
                          </Stack>
                          <Typography variant="body2">{entry.text}</Typography>
                          <Divider sx={{ mt: 1 }} />
                        </Box>
                      ))}
                    </Stack>
                  ) : (
                    <Typography variant="body2">No hay transcripcion cargada.</Typography>
                  )}
                </CardContent>
              </Card>
            </Grid>
            <Grid item xs={12} md={6}>
              <Card>
                <CardHeader title="Metricas de runtime" />
                <CardContent>
                  {metrics ? (
                    <Stack spacing={1}>
                      <Typography variant="body2">Modalidad: {metrics.runtimeKind}</Typography>
                      <Typography variant="body2">Sesiones totales: {metrics.totalSessions}</Typography>
                      <Typography variant="body2">Sesiones activas: {metrics.activeSessions}</Typography>
                      <Typography variant="body2">Sesiones completadas: {metrics.completedSessions}</Typography>
                      <Typography variant="body2">Tasa de exito: {metrics.successRatePercent}%</Typography>
                      <Typography variant="body2">Latencia promedio punta a punta: {metrics.avgE2eLatencyMs} ms</Typography>
                      <Typography variant="body2">Eventos con error: {metrics.totalErrorEvents}</Typography>
                    </Stack>
                  ) : (
                    <Typography variant="body2">Aun no se cargan metricas.</Typography>
                  )}
                </CardContent>
              </Card>
            </Grid>
          </Grid>
          <Card>
            <CardHeader title="Trazabilidad" />
            <CardContent>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
                <TextField
                  select
                  label="Etapa"
                  value={timelineStageFilter}
                  onChange={(e) => setTimelineStageFilter(e.target.value)}
                  size="small"
                >
                  <MenuItem value="all">Todas</MenuItem>
                  {[...new Set(timeline.map((item) => item.stage))].map((stage) => (
                    <MenuItem key={stage} value={stage}>{stage}</MenuItem>
                  ))}
                </TextField>
                <TextField
                  select
                  label="Estado"
                  value={timelineStatusFilter}
                  onChange={(e) => setTimelineStatusFilter(e.target.value)}
                  size="small"
                >
                  <MenuItem value="all">Todos</MenuItem>
                  {[...new Set(timeline.map((item) => item.status))].map((status) => (
                    <MenuItem key={status} value={status}>{status}</MenuItem>
                  ))}
                </TextField>
              </Stack>
              <Stack spacing={1}>
                {filteredTimeline.map((event, index) => (
                  <Box key={`${event.timestamp}-${index}`}>
                    <Typography variant="body2">
                      [{new Date(event.timestamp).toLocaleString()}] {event.stage} � {event.status} � {event.direction} � {event.payloadType}
                    </Typography>
                    {event.message && (
                      <Typography variant="caption">{mapError(event.message) ?? event.message}</Typography>
                    )}
                    <Divider sx={{ mt: 1 }} />
                  </Box>
                ))}
                {!filteredTimeline.length && (
                  <Typography variant="body2">No hay eventos para los filtros actuales.</Typography>
                )}
              </Stack>
            </CardContent>
          </Card>
        </Stack>
      </DashboardContent>
    </>
  );
}
