import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TextField from '@mui/material/TextField';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import CircularProgress from '@mui/material/CircularProgress';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';



interface Channel {
  id: string;
  name: string;
  type: string;
  status: string;
  config: Record<string, string>;
  createdAt: string;
  lastActivityAt?: string;
}

interface ChannelSession {
  id: string;
  channelId: string;
  channelType: string;
  identifier: string;
  agentId?: string;
  threadId?: string;
  status: string;
  messageCount: number;
  createdAt: string;
  lastActivityAt: string;
  expiresAt?: string;
}

interface SessionMessageEvidence {
  id: string;
  direction: string;
  content: string;
  createdAt: string;
  status: string;
  agentExecutionId?: string;
  channelMessageIdIn?: string;
  channelMessageIdOut?: string;
}

export default function ChannelsPage() {
  const TENANT_ID = useTenantId();
  const [channels, setChannels] = useState<Channel[]>([]);
  const [sessions, setSessions] = useState<ChannelSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [openCreate, setOpenCreate] = useState(false);
  const [qrCode, setQrCode] = useState<string | null>(null);
  const [selectedChannel, setSelectedChannel] = useState<Channel | null>(null);
  const [qrPolling, setQrPolling] = useState(false);
  const [qrPollRounds, setQrPollRounds] = useState(0);
  const [selectedSession, setSelectedSession] = useState<ChannelSession | null>(null);
  const [sessionMessages, setSessionMessages] = useState<SessionMessageEvidence[]>([]);
  const [sessionLoading, setSessionLoading] = useState(false);
  const [candidateAgents, setCandidateAgents] = useState<{ id: string; name: string }[]>([]);
  const [openRouting, setOpenRouting] = useState(false);
  const [routingChannel, setRoutingChannel] = useState<Channel | null>(null);
  const [routingForm, setRoutingForm] = useState({ defaultAgentId: '', routingAgentsCsv: '', routingCapacitiesCsv: '' });
  const [routingPreview, setRoutingPreview] = useState<{ suggestedAgentId?: string; activeLoadByAgent?: Record<string, number> } | null>(null);

  const [form, setForm] = useState({
    name: '',
    type: 'WhatsApp',
    authMode: 'qr',
    apiToken: '',
    phoneNumberId: '',
    defaultAgentId: '',
    routingAgentsCsv: '',
    routingCapacitiesCsv: '',
  });

  const channelTypes = [
    { value: 'WhatsApp', label: 'WhatsApp', icon: 'mdi:whatsapp' },
    { value: 'WebChat', label: 'Web Chat', icon: 'mdi:web' },
    { value: 'Api', label: 'API Direct', icon: 'mdi:api' },
    { value: 'Telegram', label: 'Telegram', icon: 'mdi:telegram' },
    { value: 'Slack', label: 'Slack', icon: 'mdi:slack' },
  ];

  const fetchAll = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [channelsRes, sessionsRes, agentsRes] = await Promise.all([
        axios.get(endpoints.agentflow.channels.list(TENANT_ID)),
        axios.get(`/api/v1/tenants/${TENANT_ID}/channel-sessions?limit=50`),
        axios.get(`/api/v1/tenants/${TENANT_ID}/agents`),
      ]);

      setChannels((channelsRes.data ?? []) as Channel[]);
      setSessions((sessionsRes.data ?? []) as ChannelSession[]);
      const agents = (agentsRes.data ?? [])
        .filter((a: any) => a?.id && a.status !== 'Archived')
        .map((a: any) => ({ id: a.id, name: a.name }));
      setCandidateAgents(agents);
    } catch (err: any) {
      setError(err?.message || 'Error cargando canales');
    } finally {
      setLoading(false);
    }
  }, [TENANT_ID]);

  useEffect(() => {
    fetchAll();
  }, [fetchAll]);

  const handleCreate = async () => {
    if (!form.name.trim()) return;

    try {
      setSaving(true);
      const config: Record<string, string> = {
        AuthMode: form.authMode,
        DefaultAgentId: form.defaultAgentId || 'default-agent',
        RoutingAgents: form.routingAgentsCsv || '',
      };

      if (form.authMode === 'business') {
        config.ApiToken = form.apiToken;
        config.PhoneNumberId = form.phoneNumberId;
      }

      await axios.post(endpoints.agentflow.channels.create(TENANT_ID), {
        name: form.name.trim(),
        type: form.type,
        config,
      });

      setOpenCreate(false);
      setForm({ name: '', type: 'WhatsApp', authMode: 'qr', apiToken: '', phoneNumberId: '', defaultAgentId: '', routingAgentsCsv: '', routingCapacitiesCsv: '' });
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Error creando canal');
    } finally {
      setSaving(false);
    }
  };

  const openRoutingDialog = async (channel: Channel) => {
    try {
      const res = await axios.get(endpoints.agentflow.channels.routingGet(TENANT_ID, channel.id));
      const routingAgents = (res.data?.routingAgents ?? []) as string[];
      setRoutingChannel(channel);
      setRoutingForm({
        defaultAgentId: res.data?.defaultAgentId || channel.config?.DefaultAgentId || '',
        routingAgentsCsv: routingAgents.join(','),
        routingCapacitiesCsv: Object.entries(res.data?.routingCapacities || {}).map(([agentId, cap]) => `${agentId}:${cap}`).join(','),
      });
      setRoutingPreview(null);
      setOpenRouting(true);
    } catch (err: any) {
      alert(err?.message || 'Error cargando reglas de enrutamiento');
    }
  };

  const saveRouting = async () => {
    if (!routingChannel) return;
    try {
      setSaving(true);
      await axios.post(endpoints.agentflow.channels.routingUpdate(TENANT_ID, routingChannel.id), {
        defaultAgentId: routingForm.defaultAgentId,
        routingAgents: routingForm.routingAgentsCsv
          .split(',')
          .map((x) => x.trim())
          .filter(Boolean),
        routingCapacities: routingForm.routingCapacitiesCsv
          .split(',')
          .map((entry) => entry.trim())
          .filter(Boolean)
          .reduce<Record<string, number>>((acc, entry) => {
            const [agentId, capRaw] = entry.split(':').map((x) => x.trim());
            const cap = Number(capRaw);
            if (agentId && Number.isFinite(cap) && cap > 0) acc[agentId] = cap;
            return acc;
          }, {}),
      });
      setOpenRouting(false);
      setRoutingChannel(null);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Error actualizando enrutamiento');
    } finally {
      setSaving(false);
    }
  };

  const runRoutingPreview = async () => {
    if (!routingChannel) return;
    try {
      const res = await axios.get(endpoints.agentflow.channels.routingPreview(TENANT_ID, routingChannel.id));
      setRoutingPreview({
        suggestedAgentId: res.data?.suggestedAgentId,
        activeLoadByAgent: res.data?.activeLoadByAgent || {},
      });
    } catch (err: any) {
      alert(err?.message || 'Error ejecutando vista previa de enrutamiento');
    }
  };

  const fetchQrCode = async (channelId: string) => {
    const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}/qr`);
    return res.data?.qrCode as string | undefined;
  };

  const handleActivate = async (channel: Channel) => {
    try {
      await axios.post(`/api/v1/tenants/${TENANT_ID}/channels/${channel.id}/activate`);

      if (channel.type === 'WhatsApp' && channel.config?.AuthMode === 'qr') {
        setSelectedChannel(channel);

        try {
          const qr = await fetchQrCode(channel.id);
          if (qr) setQrCode(qr);
        } catch {
          // ignore; polling below handles pending state
        }

        await startFiniteQrPolling(channel.id);
      }

      await fetchAll();
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Error activando canal');
    }
  };

  const handleDeactivate = async (channelId: string) => {
    if (!confirm('Desactivar este canal?')) return;

    try {
      await axios.post(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}/deactivate`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Error desactivando canal');
    }
  };

  const handleDelete = async (channelId: string) => {
    if (!confirm('Eliminar este canal permanentemente?')) return;

    try {
      await axios.delete(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Error eliminando canal');
    }
  };

  const handleCheckHealth = async (channel: Channel) => {
    try {
      const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channels/${channel.id}/status`);
      const qrSuffix = channel.type === 'WhatsApp' && channel.config?.AuthMode === 'qr'
        ? ` | QR: ${res.data.healthy ? 'CONNECTED' : (res.data.qrAvailable ? 'AVAILABLE' : 'PENDING')}`
        : '';
      alert(`Health: ${res.data.healthy ? 'OK' : 'UNHEALTHY'} - ${res.data.message || 'n/a'}${qrSuffix}`);
    } catch (err: any) {
      alert(err?.message || 'Error en health check');
    }
  };

  const openSessionEvidence = async (session: ChannelSession) => {
    try {
      setSelectedSession(session);
      setSessionLoading(true);
      const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channel-sessions/${session.id}/messages?limit=50`);
      setSessionMessages((res.data ?? []) as SessionMessageEvidence[]);
    } catch (err: any) {
      alert(err?.message || 'Error cargando mensajes de sesion');
    } finally {
      setSessionLoading(false);
    }
  };

  const startFiniteQrPolling = async (channelId: string) => {
    setQrPolling(true);
    setQrPollRounds(0);

    const maxRounds = 10;
    for (let round = 1; round <= maxRounds; round++) {
      setQrPollRounds(round);
      try {
        const statusRes = await axios.get(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}/status`);
        if (statusRes.data?.healthy) {
          setQrPolling(false);
          setQrCode(null);
          setError(null);
          return;
        }

        const qr = await fetchQrCode(channelId);
        if (qr) {
          setQrCode(qr);
        }
      } catch {
        // ignore transient poll errors
      }

      await new Promise((r) => setTimeout(r, 2000));
    }

    setQrPolling(false);
    setError('Finalizo el polling de QR. Si sigue pendiente, usa Refrescar QR.');
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Active': return 'success';
      case 'Error': return 'error';
      case 'Maintenance': return 'warning';
      default: return 'default';
    }
  };

  const latencyMs = sessionMessages.length >= 2
    ? Math.max(0, new Date(sessionMessages[sessionMessages.length - 1].createdAt).getTime() - new Date(sessionMessages[0].createdAt).getTime())
    : 0;

  const firstExecutionId = sessionMessages.find((m) => m.agentExecutionId)?.agentExecutionId;
  const messageIdIn = sessionMessages.find((m) => m.channelMessageIdIn)?.channelMessageIdIn;
  const messageIdOut = sessionMessages.find((m) => m.channelMessageIdOut)?.channelMessageIdOut;

  return (
    <>
      <Helmet>
        <title>Channels | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Connect - Canales de comunicacion</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
              Administra conexiones, salud del canal y evidencia operacional.
            </Typography>
          </Box>
          <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={() => setOpenCreate(true)}>
            Agregar canal
          </Button>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={7}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Canales conectados</Typography>
              {loading ? (
                <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
              ) : channels.length === 0 ? (
                <Alert severity="info">Aun no hay canales configurados.</Alert>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Nombre</TableCell>
                      <TableCell>Tipo</TableCell>
                      <TableCell>Estado</TableCell>
                      <TableCell>Actividad</TableCell>
                      <TableCell align="right">Acciones</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {channels.map((c) => (
                      <TableRow key={c.id} hover>
                        <TableCell>{c.name}</TableCell>
                        <TableCell>
                          <Chip label={c.type} size="small" variant="outlined" />
                        </TableCell>
                        <TableCell>
                          <Chip label={c.status} size="small" color={getStatusColor(c.status) as any} />
                        </TableCell>
                        <TableCell>
                          {c.lastActivityAt ? new Date(c.lastActivityAt).toLocaleString() : 'Nunca'}
                        </TableCell>
                        <TableCell align="right">
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            {c.status === 'Active' ? (
                              <Button size="small" variant="outlined" color="warning" onClick={() => handleDeactivate(c.id)}>
                                Desactivar
                              </Button>
                            ) : (
                              <Button size="small" variant="outlined" color="success" onClick={() => handleActivate(c)}>
                                Activar
                              </Button>
                            )}
                            <IconButton size="small" onClick={() => handleCheckHealth(c)}>
                              <Iconify icon="mdi:heart-pulse" />
                            </IconButton>
                            <IconButton size="small" onClick={() => openRoutingDialog(c)}>
                              <Iconify icon="solar:settings-bold" />
                            </IconButton>
                            <IconButton size="small" color="error" onClick={() => handleDelete(c.id)}>
                              <Iconify icon="mingcute:delete-line" />
                            </IconButton>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Card>
          </Grid>

          <Grid item xs={12} md={5}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Sesiones activas</Typography>
              {sessions.length === 0 ? (
                <Alert severity="info">No hay sesiones activas.</Alert>
              ) : (
                <Stack spacing={2} sx={{ maxHeight: 600, overflow: 'auto' }}>
                  {sessions.slice(0, 10).map((s) => (
                    <Box key={s.id} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                        <Chip label={s.channelType} size="small" />
                        <Typography variant="caption">{s.messageCount} msgs</Typography>
                      </Box>
                      <Typography variant="body2" fontWeight={700}>{s.identifier}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Agente: {s.agentId ?? '-'} â€¢ Hilo: {s.threadId?.slice(0, 8) ?? '-'}
                      </Typography>
                      <Typography variant="caption" display="block" sx={{ mt: 0.5 }}>
                        Ultima: {new Date(s.lastActivityAt).toLocaleString()}
                      </Typography>
                      <Button size="small" sx={{ mt: 1 }} onClick={() => openSessionEvidence(s)}>
                        Ver evidencia
                      </Button>
                    </Box>
                  ))}
                </Stack>
              )}
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      {/* Create Channel Dialog */}
      <Dialog open={openCreate} onClose={() => setOpenCreate(false)} fullWidth maxWidth="sm">
        <DialogTitle>Crear canal</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <TextField
              label="Nombre del canal"
              value={form.name}
              onChange={(e) => setForm((p) => ({ ...p, name: e.target.value }))}
              placeholder="WhatsApp Support"
              fullWidth
            />

            <TextField
              select
              label="Tipo de canal"
              value={form.type}
              onChange={(e) => setForm((p) => ({ ...p, type: e.target.value }))}
              fullWidth
            >
              {channelTypes.map((t) => (
                <MenuItem key={t.value} value={t.value}>
                  {t.label}
                </MenuItem>
              ))}
            </TextField>

            {form.type === 'WhatsApp' && (
              <>
                <TextField
                  select
                  label="Modo de autenticacion"
                  value={form.authMode}
                  onChange={(e) => setForm((p) => ({ ...p, authMode: e.target.value }))}
                  fullWidth
                >
                  <MenuItem value="qr">Codigo QR (tipo OpenClaw)</MenuItem>
                  <MenuItem value="business">WhatsApp Business API</MenuItem>
                </TextField>

                {form.authMode === 'business' && (
                  <>
                    <TextField
                      label="API Token"
                      value={form.apiToken}
                      onChange={(e) => setForm((p) => ({ ...p, apiToken: e.target.value }))}
                      placeholder="EAAB..."
                      fullWidth
                      type="password"
                    />
                    <TextField
                      label="ID de numero telefonico"
                      value={form.phoneNumberId}
                      onChange={(e) => setForm((p) => ({ ...p, phoneNumberId: e.target.value }))}
                      placeholder="123456789"
                      fullWidth
                    />
                  </>
                )}
              </>
            )}

            <TextField
              select
              label="Agente por defecto"
              value={form.defaultAgentId}
              onChange={(e) => setForm((p) => ({ ...p, defaultAgentId: e.target.value }))}
              fullWidth
              helperText={candidateAgents.length === 0 ? 'No hay agentes disponibles' : 'Selecciona el agente por defecto para este canal'}
            >
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>
                  {agent.name} ({agent.id})
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Agentes de enrutamiento (IDs separados por coma)"
              value={form.routingAgentsCsv}
              onChange={(e) => setForm((p) => ({ ...p, routingAgentsCsv: e.target.value }))}
              fullWidth
              helperText="Se usa round-robin por carga actual. Ejemplo: sales-agent,support-agent"
            />
            <TextField
              label="Capacidades de enrutamiento (agentId:max, CSV)"
              value={form.routingCapacitiesCsv}
              onChange={(e) => setForm((p) => ({ ...p, routingCapacitiesCsv: e.target.value }))}
              fullWidth
              helperText="Maximo opcional de sesiones activas por agente. Ejemplo: sales-agent:20,support-agent:15"
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenCreate(false)}>Cancelar</Button>
          <Button variant="contained" onClick={handleCreate} disabled={saving || !form.name}>
            {saving ? 'Creando...' : 'Crear'}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={openRouting} onClose={() => setOpenRouting(false)} fullWidth maxWidth="sm">
        <DialogTitle>Reglas de enrutamiento - {routingChannel?.name}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <TextField
              select
              label="Agente por defecto"
              value={routingForm.defaultAgentId}
              onChange={(e) => setRoutingForm((prev) => ({ ...prev, defaultAgentId: e.target.value }))}
              fullWidth
            >
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>
                  {agent.name} ({agent.id})
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Agentes de enrutamiento (IDs separados por coma)"
              value={routingForm.routingAgentsCsv}
              onChange={(e) => setRoutingForm((prev) => ({ ...prev, routingAgentsCsv: e.target.value }))}
              fullWidth
              helperText="Estos agentes se usan para asignacion automatica por menor carga activa."
            />
            <TextField
              label="Capacidades de enrutamiento (agentId:max, CSV)"
              value={routingForm.routingCapacitiesCsv}
              onChange={(e) => setRoutingForm((prev) => ({ ...prev, routingCapacitiesCsv: e.target.value }))}
              fullWidth
              helperText="Limites de capacidad por agente. Ejemplo: a1:20,a2:15"
            />
            {routingPreview && (
              <Alert severity="info">
                Sugerido: {routingPreview.suggestedAgentId || 'N/A'} | Carga: {Object.entries(routingPreview.activeLoadByAgent || {}).map(([a, l]) => `${a}=${l}`).join(', ') || 'N/A'}
              </Alert>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={runRoutingPreview}>Vista previa de proxima asignacion</Button>
          <Button onClick={() => setOpenRouting(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveRouting} disabled={saving}>
            Guardar enrutamiento
          </Button>
        </DialogActions>
      </Dialog>

      {/* QR Code Dialog */}
      <Dialog open={!!qrCode} onClose={() => setQrCode(null)} maxWidth="sm">
        <DialogTitle>Escanear codigo QR - {selectedChannel?.name}</DialogTitle>
        <DialogContent>
          <Box sx={{ textAlign: 'center', py: 3 }}>
            {qrCode && (
              <>
                <img src={qrCode} alt="WhatsApp QR" style={{ maxWidth: '100%', height: 'auto' }} />
                <Alert severity="info" sx={{ mt: 2 }}>
                  Abre WhatsApp en tu telefono, luego ve a Configuracion, Dispositivos vinculados, Vincular dispositivo y escanea este codigo QR.
                </Alert>
              </>
            )}
            {qrPolling && (
              <Alert severity="warning" sx={{ mt: 2 }}>
                Esperando confirmacion de conexion... intento {qrPollRounds}/10
              </Alert>
            )}
          </Box>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={async () => {
              if (!selectedChannel) return;
              try {
                const qr = await fetchQrCode(selectedChannel.id);
                if (qr) {
                  setQrCode(qr);
                  setError(null);
                } else {
                  setError('QR still not available. Check bridge connection and try again.');
                }
              } catch {
                setError('QR still not available. Check bridge connection and try again.');
              }
            }}
            disabled={qrPolling}
          >
            Refrescar QR
          </Button>
          <Button onClick={() => setQrCode(null)}>Cerrar</Button>
        </DialogActions>
      </Dialog>

      {/* Session Evidence Dialog */}
      <Dialog open={!!selectedSession} onClose={() => setSelectedSession(null)} fullWidth maxWidth="md">
        <DialogTitle>Evidencia de sesion - {selectedSession?.identifier}</DialogTitle>
        <DialogContent>
          {sessionLoading ? (
            <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
          ) : (
            <Stack spacing={1.5}>
              <Alert severity="info">
                ExecutionId: {firstExecutionId || 'N/A'} | MsgIn: {messageIdIn || 'N/A'} | MsgOut: {messageIdOut || 'N/A'} | Latency: {latencyMs} ms
              </Alert>
              {sessionMessages.length === 0 ? (
                <Alert severity="warning">No se encontraron mensajes para esta sesion.</Alert>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Direccion</TableCell>
                      <TableCell>Contenido</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Creado</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {sessionMessages.map((m) => (
                      <TableRow key={m.id}>
                        <TableCell>{m.direction}</TableCell>
                        <TableCell sx={{ maxWidth: 420, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                          {m.content}
                        </TableCell>
                        <TableCell>{m.status}</TableCell>
                        <TableCell>{new Date(m.createdAt).toLocaleString()}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setSelectedSession(null)}>Cerrar</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

