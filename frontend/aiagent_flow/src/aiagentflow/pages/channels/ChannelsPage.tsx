import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Select from '@mui/material/Select';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import OutlinedInput from '@mui/material/OutlinedInput';
import FormHelperText from '@mui/material/FormHelperText';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

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

interface TenantConnection {
  id: string;
  name: string;
  type: string;
  connectorId: string;
  config: Record<string, string>;
  secretVersion?: number;
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
  const [connections, setConnections] = useState<TenantConnection[]>([]);
  const [openRouting, setOpenRouting] = useState(false);
  const [routingChannel, setRoutingChannel] = useState<Channel | null>(null);
  const [routingForm, setRoutingForm] = useState({ defaultAgentId: '', routingAgentIds: [] as string[] });
  const [routingPreview, setRoutingPreview] = useState<{ suggestedAgentId?: string; activeLoadByAgent?: Record<string, number> } | null>(null);
  const [openTestPanel, setOpenTestPanel] = useState(false);
  const [testPanelChannel, setTestPanelChannel] = useState<Channel | null>(null);
  const [testMsg, setTestMsg] = useState({ content: '', from: '', callbackUrl: '', asyncMode: false });
  const [testResult, setTestResult] = useState<{ status: number; data: any } | null>(null);
  const [testSending, setTestSending] = useState(false);

  const [form, setForm] = useState({
    name: '',
    type: 'WhatsApp',
    authMode: 'qr',
    apiToken: '',
    phoneNumberId: '',
    defaultAgentId: '',
    routingAgentIds: [] as string[],
    connectionId: '',
    provider: 'twilio',
    sessionWindowHours: '24',
    routerAgentId: '',
    reopenTemplateName: '',
  });

  const channelTypes = [
    { value: 'WhatsApp', label: 'WhatsApp', icon: 'mdi:whatsapp' },
    { value: 'WebChat', label: 'Web Chat', icon: 'mdi:web' },
    { value: 'Api', label: 'API Direct', icon: 'mdi:api' },
    { value: 'Voice', label: 'Voz / Twilio', icon: 'mdi:phone-in-talk-outline' },
    { value: 'CallCenter', label: 'Call Center', icon: 'mdi:account-voice' },
    { value: 'Email', label: 'Email', icon: 'mdi:email-outline' },
    { value: 'Telegram', label: 'Telegram', icon: 'mdi:telegram' },
    { value: 'Slack', label: 'Slack', icon: 'mdi:slack' },
  ];

  const fetchAll = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [channelsRes, sessionsRes, agentsRes, connectionsRes] = await Promise.all([
        axios.get(endpoints.agentflow.channels.list(TENANT_ID)),
        axios.get(`/api/v1/tenants/${TENANT_ID}/channel-sessions?limit=50`),
        axios.get(`/api/v1/tenants/${TENANT_ID}/agents`),
        axios.get(endpoints.agentflow.connections.list(TENANT_ID)).catch(() => ({ data: [] })),
      ]);

      setChannels((channelsRes.data ?? []) as Channel[]);
      setSessions((sessionsRes.data ?? []) as ChannelSession[]);
      const agents = (agentsRes.data ?? [])
        .filter((a: any) => a?.id && a.status !== 'Archived')
        .map((a: any) => ({ id: a.id, name: a.name }));
      setCandidateAgents(agents);
      setConnections((connectionsRes.data ?? []) as TenantConnection[]);
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
        RoutingAgents: form.routingAgentIds.join(','),
      };

      if (form.connectionId) config.ConnectionId = form.connectionId;
      if (form.provider) config.Provider = form.provider;

      if (form.authMode === 'business') {
        config.ApiToken = form.apiToken;
        config.PhoneNumberId = form.phoneNumberId;
      }

      await axios.post(endpoints.agentflow.channels.create(TENANT_ID), {
        name: form.name.trim(),
        type: form.type,
        config,
        sessionWindowHours: form.sessionWindowHours ? Number(form.sessionWindowHours) : undefined,
        routerAgentId: form.routerAgentId || undefined,
        reopenTemplateName: form.reopenTemplateName || undefined,
      });

      setOpenCreate(false);
      setForm({ name: '', type: 'WhatsApp', authMode: 'qr', apiToken: '', phoneNumberId: '', defaultAgentId: '', routingAgentIds: [], connectionId: '', provider: 'twilio', sessionWindowHours: '24', routerAgentId: '', reopenTemplateName: '' });
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
      setRoutingForm({
        defaultAgentId: res.data?.defaultAgentId || channel.config?.DefaultAgentId || '',
        routingAgentIds: (res.data?.routingAgents ?? []) as string[],
      });
      setRoutingChannel(channel);
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
        routingAgents: routingForm.routingAgentIds,
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

  const sendTestMessage = async () => {
    if (!testPanelChannel || !testMsg.content.trim()) return;
    try {
      setTestSending(true);
      setTestResult(null);
      const res = await axios.post(
        `/api/v1/tenants/${TENANT_ID}/channels/${testPanelChannel.id}/messages`,
        {
          content: testMsg.content,
          from: testMsg.from || undefined,
          callbackUrl: testMsg.asyncMode && testMsg.callbackUrl ? testMsg.callbackUrl : undefined,
        }
      );
      setTestResult({ status: res.status, data: res.data });
    } catch (err: any) {
      setTestResult({ status: err?.response?.status ?? 0, data: err?.response?.data ?? { error: err?.message } });
    } finally {
      setTestSending(false);
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Active': return 'success';
      case 'Error': return 'error';
      case 'Maintenance': return 'warning';
      default: return 'default';
    }
  };

  const activeChannels = channels.filter((channel) => channel.status === 'Active').length;
  const whatsappChannels = channels.filter((channel) => channel.type === 'WhatsApp').length;
  const twilioConnections = connections.filter(
    (connection) =>
      connection.connectorId === 'twilio' ||
      connection.config?.provider === 'twilio' ||
      connection.type === 'Messaging'
  );
  const channelCapabilities = (channel: Channel) => {
    if (channel.type === 'WhatsApp') return ['templates', 'inbox', 'qr/auth'];
    if (channel.type === 'Voice') return ['voice', 'twilio', 'outbound'];
    if (channel.type === 'CallCenter') return ['campaigns', 'voice', 'handoff'];
    if (channel.type === 'Email') return ['inbox', 'outbound'];
    if (channel.type === 'WebChat') return ['inbox', 'web widget'];
    if (channel.type === 'Api') return ['webhook', 'inbox'];
    return ['inbox'];
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
        <title>Conectores de canal | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper
          variant="outlined"
          sx={{
            mb: 3,
            p: { xs: 2.5, md: 3 },
            borderRadius: 4,
            background:
              'radial-gradient(circle at 8% 18%, rgba(0,167,181,0.14), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ md: 'center' }}>
            <Stack direction="row" spacing={1.5} alignItems="center">
              <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.lighter', color: 'primary.main' }}>
                <Iconify icon="mdi:access-point" width={30} />
              </Avatar>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Canales omnicanal
                </Typography>
                <Typography variant="h3">Conectores de canal</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                  Conecta WhatsApp, web chat, voz, call center, email y APIs para usarlos en Inbox y workflows.
                </Typography>
              </Box>
            </Stack>
            <Stack direction="row" spacing={1}>
              <Button
                variant="outlined"
                component={RouterLink}
                href={paths.dashboard.marketplace}
                startIcon={<Iconify icon="mdi:connection" />}
              >
                Integraciones
              </Button>
              <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={() => setOpenCreate(true)}>
                Agregar canal
              </Button>
            </Stack>
          </Stack>
        </Paper>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12}>
            <Grid container spacing={2}>
              {[
                ['Canales', channels.length, 'Configurados', 'mdi:message-processing-outline'],
                ['Activos', activeChannels, 'Listos para operar', 'mdi:check-decagram-outline'],
                ['WhatsApp', whatsappChannels, 'Templates y QR', 'mdi:whatsapp'],
                ['Sesiones', sessions.length, 'Conversaciones recientes', 'mdi:inbox-outline'],
              ].map(([label, value, helper, icon]) => (
                <Grid item xs={12} sm={6} md={3} key={label}>
                  <Card variant="outlined" sx={{ p: 2, height: '100%' }}>
                    <Stack direction="row" spacing={1.5} alignItems="center">
                      <Box
                        sx={{
                          width: 40,
                          height: 40,
                          borderRadius: 1.5,
                          display: 'grid',
                          placeItems: 'center',
                          bgcolor: 'primary.lighter',
                          color: 'primary.main',
                        }}
                      >
                        <Iconify icon={String(icon)} width={23} />
                      </Box>
                      <Box>
                        <Typography variant="h5">{String(value)}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {label} Â· {helper}
                        </Typography>
                      </Box>
                    </Stack>
                  </Card>
                </Grid>
              ))}
            </Grid>
          </Grid>

          <Grid item xs={12}>
            <Card variant="outlined" sx={{ p: 2, borderRadius: 2.5 }}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between">
                <Box>
                  <Typography variant="h6">Twilio omnicanal</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Una sola conexion Twilio se reutiliza para Voz, Call Center, SMS y futuros canales WhatsApp por Twilio.
                    Los canales no guardan secretos: solo referencian la conexion del tenant.
                  </Typography>
                </Box>
                <Stack direction="row" spacing={0.8} flexWrap="wrap" alignItems="center">
                  <Chip
                    color={twilioConnections.length > 0 ? 'success' : 'warning'}
                    label={twilioConnections.length > 0 ? `${twilioConnections.length} conexion(es) Twilio` : 'Twilio pendiente'}
                  />
                  <Button
                    variant="outlined"
                    component={RouterLink}
                    href={paths.dashboard.marketplace}
                    startIcon={<Iconify icon="mdi:phone-settings-outline" />}
                  >
                    Configurar Twilio
                  </Button>
                </Stack>
              </Stack>
            </Card>
          </Grid>

          <Grid item xs={12} md={7}>
            <Card variant="outlined" sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 0.5 }}>Canales conectados</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Estos conectores alimentan Inbox y aparecen como integraciones disponibles en Workflow Studio.
              </Typography>
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
                        <TableCell>Capacidades</TableCell>
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
                          <Stack direction="row" spacing={0.5} flexWrap="wrap">
                            {channelCapabilities(c).map((capability) => (
                              <Chip key={capability} label={capability} size="small" />
                            ))}
                          </Stack>
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
                            <IconButton size="small" title="Probar mensajes" onClick={() => { setTestPanelChannel(c); setTestResult(null); setTestMsg({ content: '', from: '', callbackUrl: '', asyncMode: false }); setOpenTestPanel(true); }}>
                              <Iconify icon="mdi:message-flash-outline" />
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
            <Card variant="outlined" sx={{ p: 2 }}>
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
                        Agente: {s.agentId ?? '-'} Â· Hilo: {s.threadId?.slice(0, 8) ?? '-'}
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

            {(form.type === 'Voice' || form.type === 'CallCenter') && (
              <>
                <Alert severity={twilioConnections.length > 0 ? 'success' : 'warning'}>
                  Este canal usa una conexion Twilio reusable. Configurala una vez en Marketplace/Integraciones y
                  reutilizala para voz, call center, campanas y nodos de Workflow Studio.
                </Alert>
                <TextField
                  select
                  label="Conexion Twilio"
                  value={form.connectionId}
                  onChange={(e) => setForm((p) => ({ ...p, connectionId: e.target.value, provider: 'twilio' }))}
                  fullWidth
                  helperText={connections.length === 0 ? 'No hay conexiones creadas. Ve a Marketplace para crear Twilio.' : 'Se reutiliza para todos los canales de voz.'}
                >
                  <MenuItem value="">Detectar por provider twilio</MenuItem>
                  {connections
                    .filter((connection) => connection.connectorId === 'twilio' || connection.config?.provider === 'twilio' || connection.type === 'Messaging')
                    .map((connection) => (
                      <MenuItem key={connection.id} value={connection.id}>
                        {connection.name} ({connection.connectorId})
                      </MenuItem>
                    ))}
                </TextField>
              </>
            )}

            <TextField
              select
              label="Agente por defecto"
              value={form.defaultAgentId}
              onChange={(e) => setForm((p) => ({ ...p, defaultAgentId: e.target.value }))}
              fullWidth
              helperText={candidateAgents.length === 0 ? 'No hay agentes disponibles' : 'Agente que responde cuando no hay router configurado'}
            >
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>
                  {agent.name}
                </MenuItem>
              ))}
            </TextField>

            <Divider textAlign="left">
              <Typography variant="caption" color="text.secondary">Enrutamiento por carga (opcional)</Typography>
            </Divider>

            <FormControl fullWidth>
              <InputLabel>Agentes de enrutamiento</InputLabel>
              <Select
                multiple
                value={form.routingAgentIds}
                onChange={(e) => setForm((p) => ({ ...p, routingAgentIds: e.target.value as string[] }))}
                input={<OutlinedInput label="Agentes de enrutamiento" />}
                renderValue={(selected) => (
                  <Stack direction="row" spacing={0.5} flexWrap="wrap">
                    {(selected as string[]).map((id) => {
                      const agent = candidateAgents.find((a) => a.id === id);
                      return <Chip key={id} label={agent?.name || id} size="small" />;
                    })}
                  </Stack>
                )}
              >
                {candidateAgents.map((agent) => (
                  <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
                ))}
              </Select>
              <FormHelperText>Asignación automática por menor carga activa. Deja vacío para usar solo el agente por defecto.</FormHelperText>
            </FormControl>

            {form.type === 'WhatsApp' && (
              <>
                <TextField
                  label="Ventana de sesion (horas)"
                  type="number"
                  value={form.sessionWindowHours}
                  onChange={(e) => setForm((p) => ({ ...p, sessionWindowHours: e.target.value }))}
                  fullWidth
                  helperText="Horas que la sesion WhatsApp permanece abierta. Default: 24"
                  inputProps={{ min: 1, max: 168 }}
                />
                <TextField
                  select
                  label="Agente Router (intent routing)"
                  value={form.routerAgentId}
                  onChange={(e) => setForm((p) => ({ ...p, routerAgentId: e.target.value }))}
                  fullWidth
                  helperText="Agente con rol Router que clasifica intenciones antes de asignar un workflow."
                >
                  <MenuItem value="">Sin Router (usa agente por defecto)</MenuItem>
                  {candidateAgents.map((agent) => (
                    <MenuItem key={agent.id} value={agent.id}>
                      {agent.name}
                    </MenuItem>
                  ))}
                </TextField>
                <TextField
                  label="Template de reapertura (WhatsApp)"
                  value={form.reopenTemplateName}
                  onChange={(e) => setForm((p) => ({ ...p, reopenTemplateName: e.target.value }))}
                  fullWidth
                  helperText="Nombre del template aprobado para reabrir la ventana expirada. Ejemplo: session_reopen"
                />
              </>
            )}
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
        <DialogTitle>Enrutamiento — {routingChannel?.name}</DialogTitle>
        <DialogContent>
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            <Alert severity="info" sx={{ mb: 0 }}>
              El <strong>agente por defecto</strong> responde cuando no hay coincidencia de intención. Los <strong>agentes de enrutamiento</strong> se asignan automáticamente por menor carga activa.
            </Alert>

            <TextField
              select
              label="Agente por defecto"
              value={routingForm.defaultAgentId}
              onChange={(e) => setRoutingForm((prev) => ({ ...prev, defaultAgentId: e.target.value }))}
              fullWidth
              helperText="Agente de fallback cuando ninguna intención hace match"
            >
              <MenuItem value=""><em>Sin agente por defecto</em></MenuItem>
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
              ))}
            </TextField>

            <FormControl fullWidth>
              <InputLabel>Agentes de enrutamiento</InputLabel>
              <Select
                multiple
                value={routingForm.routingAgentIds}
                onChange={(e) => setRoutingForm((prev) => ({ ...prev, routingAgentIds: e.target.value as string[] }))}
                input={<OutlinedInput label="Agentes de enrutamiento" />}
                renderValue={(selected) => (
                  <Stack direction="row" spacing={0.5} flexWrap="wrap">
                    {(selected as string[]).map((id) => {
                      const agent = candidateAgents.find((a) => a.id === id);
                      return <Chip key={id} label={agent?.name || id} size="small" />;
                    })}
                  </Stack>
                )}
              >
                {candidateAgents.map((agent) => (
                  <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
                ))}
              </Select>
              <FormHelperText>Se asignarán en round-robin por menor número de sesiones activas.</FormHelperText>
            </FormControl>

            {routingPreview && (
              <Alert severity="success">
                <strong>Próxima asignación:</strong> {candidateAgents.find(a => a.id === routingPreview.suggestedAgentId)?.name || routingPreview.suggestedAgentId || 'N/A'}
                {Object.keys(routingPreview.activeLoadByAgent || {}).length > 0 && (
                  <Typography variant="caption" display="block" sx={{ mt: 0.5 }}>
                    Carga actual: {Object.entries(routingPreview.activeLoadByAgent || {}).map(([a, l]) => {
                      const name = candidateAgents.find(ca => ca.id === a)?.name || a;
                      return `${name}: ${l}`;
                    }).join(' · ')}
                  </Typography>
                )}
              </Alert>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={runRoutingPreview} startIcon={<Iconify icon="mdi:eye-outline" />}>Vista previa</Button>
          <Button onClick={() => setOpenRouting(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveRouting} disabled={saving}>
            {saving ? 'Guardando...' : 'Guardar'}
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

      {/* Test Message Dialog */}
      <Dialog open={openTestPanel} onClose={() => setOpenTestPanel(false)} fullWidth maxWidth="sm">
        <DialogTitle>Probar mensaje — {testPanelChannel?.name}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Alert severity="info">
              Envia un mensaje al canal via <code>POST /channels/{'{channelId}'}/messages</code>. En modo sincrono el resultado aparece aqui; en modo async el canal procesara en background y responde 202.
            </Alert>
            <TextField
              label="Contenido del mensaje"
              value={testMsg.content}
              onChange={(e) => setTestMsg((p) => ({ ...p, content: e.target.value }))}
              multiline
              minRows={2}
              fullWidth
              placeholder="Hola, quiero agendar una cita"
            />
            <TextField
              label="Remitente (from)"
              value={testMsg.from}
              onChange={(e) => setTestMsg((p) => ({ ...p, from: e.target.value }))}
              fullWidth
              placeholder="+521234567890 o user@test.com"
              helperText="Opcional. Simula el identificador del cliente."
            />
            <Stack direction="row" spacing={1} alignItems="center">
              <Chip
                label={testMsg.asyncMode ? 'Modo: Async (202)' : 'Modo: Sync (200)'}
                color={testMsg.asyncMode ? 'info' : 'default'}
                onClick={() => setTestMsg((p) => ({ ...p, asyncMode: !p.asyncMode }))}
                clickable
              />
            </Stack>
            {testMsg.asyncMode && (
              <TextField
                label="Callback URL (opcional)"
                value={testMsg.callbackUrl}
                onChange={(e) => setTestMsg((p) => ({ ...p, callbackUrl: e.target.value }))}
                fullWidth
                placeholder="https://webhook.site/xxx"
                helperText="El resultado se entregara en este endpoint."
              />
            )}
            {testResult && (
              <Alert severity={testResult.status >= 200 && testResult.status < 300 ? 'success' : 'error'}>
                <Typography variant="caption" display="block">HTTP {testResult.status}</Typography>
                <Box component="pre" sx={{ fontSize: 11, maxHeight: 200, overflow: 'auto', mt: 0.5, whiteSpace: 'pre-wrap' }}>
                  {JSON.stringify(testResult.data, null, 2)}
                </Box>
              </Alert>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenTestPanel(false)}>Cerrar</Button>
          <Button variant="contained" onClick={sendTestMessage} disabled={testSending || !testMsg.content.trim()}>
            {testSending ? 'Enviando...' : 'Enviar mensaje'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
