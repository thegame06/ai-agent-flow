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
import Checkbox from '@mui/material/Checkbox';
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
import { alpha, useTheme } from '@mui/material/styles';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import OutlinedInput from '@mui/material/OutlinedInput';
import FormHelperText from '@mui/material/FormHelperText';
import TablePagination from '@mui/material/TablePagination';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { TermHelp } from 'src/aiagentflow/components/TermHelp';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';

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
  windowOpen?: boolean;
  customerKind?: string;
  displayName?: string;
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
  errorMessage?: string;
  actor?: string;
  deliveryState?: string;
}

interface ChannelIntentCatalogItem {
  key: string;
  name: string;
  description: string;
  category: string;
  priority: number;
  examples: string[];
  selected: boolean;
}

interface WorkforceQueueOption {
  id: string;
  name: string;
  active: boolean;
}

const getErrorMessage = (err: any, fallback: string) =>
  err?.response?.data?.message || err?.response?.data?.error || err?.message || fallback;

export default function ChannelsPage() {
  const theme = useTheme();
  const TENANT_ID = useTenantId();
  const [channels, setChannels] = useState<Channel[]>([]);
  const [sessions, setSessions] = useState<ChannelSession[]>([]);
  const [sessionsTotal, setSessionsTotal] = useState(0);
  const [sessionsPage, setSessionsPage] = useState(0);
  const [sessionsPageSize, setSessionsPageSize] = useState(10);
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
  const [queueOptions, setQueueOptions] = useState<WorkforceQueueOption[]>([]);
  const [routingForm, setRoutingForm] = useState({
    defaultAgentId: '',
    routingAgentIds: [] as string[],
    noMatchAction: 'human_review_only',
    routerFallbackAgentId: '',
    maxClarificationTurns: 2,
    escalationTarget: '',
    clarificationQuestions: [
      { text: 'Que necesitas resolver hoy?', active: true, field: 'motivo', required: true, retries: 1, noResponseAction: 'continue' },
      { text: 'Que resultado esperas obtener?', active: true, field: 'objetivo', required: false, retries: 1, noResponseAction: 'continue' },
      { text: 'Es algo urgente para hoy?', active: false, field: 'urgencia', required: false, retries: 1, noResponseAction: 'continue' },
    ] as Array<{ text: string; active: boolean; field: string; required: boolean; retries: number; noResponseAction: string }>,
  });
  const [routingPreview, setRoutingPreview] = useState<{ suggestedAgentId?: string; activeLoadByAgent?: Record<string, number> } | null>(null);
  const [openIntentsModal, setOpenIntentsModal] = useState(false);
  const [intentsChannel, setIntentsChannel] = useState<Channel | null>(null);
  const [intentCatalog, setIntentCatalog] = useState<ChannelIntentCatalogItem[]>([]);
  const [selectedIntentKeys, setSelectedIntentKeys] = useState<string[]>([]);
  const [loadingIntents, setLoadingIntents] = useState(false);
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
    { value: 'WebChat', label: 'Chat web', icon: 'mdi:web' },
    { value: 'Api', label: 'API directa', icon: 'mdi:api' },
    { value: 'Voice', label: 'Voz / Twilio', icon: 'mdi:phone-in-talk-outline' },
    { value: 'CallCenter', label: 'Centro de llamadas', icon: 'mdi:account-voice' },
    { value: 'Email', label: 'Email', icon: 'mdi:email-outline' },
    { value: 'Telegram', label: 'Telegram', icon: 'mdi:telegram' },
    { value: 'Stack', label: 'Stack', icon: 'mdi:slack' },
  ];

  const fetchAll = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [channelsRes, sessionsRes, agentsRes, connectionsRes, queuesRes] = await Promise.all([
        axios.get(endpoints.agentflow.channels.list(TENANT_ID)),
        axios.get(`/api/v1/tenants/${TENANT_ID}/channel-sessions?page=${sessionsPage}&pageSize=${sessionsPageSize}`),
        axios.get(`/api/v1/tenants/${TENANT_ID}/agents`),
        axios.get(endpoints.agentflow.connections.list(TENANT_ID)).catch(() => ({ data: [] })),
        axios.get(endpoints.agentflow.workforce.queues(TENANT_ID)).catch(() => ({ data: [] })),
      ]);

      setChannels((channelsRes.data ?? []) as Channel[]);
      const sessionPayload = sessionsRes.data;
      setSessions((sessionPayload?.items ?? sessionPayload ?? []) as ChannelSession[]);
      setSessionsTotal(Number(sessionPayload?.total ?? (sessionPayload ?? []).length));
      const agents = (agentsRes.data ?? [])
        .filter((a: any) => a?.id && a.status !== 'Archived')
        .map((a: any) => ({ id: a.id, name: a.name }));
      setCandidateAgents(agents);
      setConnections((connectionsRes.data ?? []) as TenantConnection[]);
      const queues = ((queuesRes.data ?? []) as WorkforceQueueOption[])
        .filter((q) => q?.id && q?.name)
        .map((q) => ({ id: q.id, name: q.name, active: Boolean(q.active) }));
      setQueueOptions(queues);
    } catch (err: any) {
      setError(err?.message || 'No se pudieron cargar los canales');
    } finally {
      setLoading(false);
    }
  }, [TENANT_ID, sessionsPage, sessionsPageSize]);

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
        IntentAgents: form.routingAgentIds.join(','),
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
      alert(err?.message || 'No se pudo crear el canal');
    } finally {
      setSaving(false);
    }
  };

  const openRoutingDialog = async (channel: Channel) => {
    try {
      const res = await axios.get(endpoints.agentflow.channels.routingGet(TENANT_ID, channel.id));
      setRoutingForm({
        defaultAgentId: res.data?.defaultAgentId || channel.config?.DefaultAgentId || '',
        routingAgentIds: ((res.data?.intentAgents ?? res.data?.routingAgents) ?? []) as string[],
        noMatchAction: res.data?.noMatchAction || 'human_review_only',
        routerFallbackAgentId: res.data?.routerFallbackAgentId || '',
        maxClarificationTurns: Number(res.data?.maxClarificationTurns || 2),
        escalationTarget: res.data?.escalationTarget || '',
        clarificationQuestions: (res.data?.clarificationQuestions && Array.isArray(res.data.clarificationQuestions) && res.data.clarificationQuestions.length > 0)
          ? res.data.clarificationQuestions
          : [
            { text: 'Que necesitas resolver hoy?', active: true, field: 'motivo', required: true, retries: 1, noResponseAction: 'continue' },
            { text: 'Que resultado esperas obtener?', active: true, field: 'objetivo', required: false, retries: 1, noResponseAction: 'continue' },
            { text: 'Es algo urgente para hoy?', active: false, field: 'urgencia', required: false, retries: 1, noResponseAction: 'continue' },
          ],
      });
      setRoutingChannel(channel);
      setRoutingPreview(null);
      setOpenRouting(true);
    } catch (err: any) {
      alert(err?.message || 'No se pudieron cargar las reglas de enrutamiento');
    }
  };

  const saveRouting = async () => {
    if (!routingChannel) return;
    try {
      setSaving(true);
      await axios.post(endpoints.agentflow.channels.routingUpdate(TENANT_ID, routingChannel.id), {
        defaultAgentId: routingForm.defaultAgentId,
        intentAgents: routingForm.routingAgentIds,
        noMatchAction: routingForm.noMatchAction,
        routerFallbackAgentId: routingForm.routerFallbackAgentId || null,
        maxClarificationTurns: routingForm.maxClarificationTurns,
        escalationTarget: routingForm.escalationTarget || null,
        clarificationQuestions: routingForm.clarificationQuestions,
      });
      setOpenRouting(false);
      setRoutingChannel(null);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'No se pudo actualizar el enrutamiento');
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
      alert(err?.message || 'No se pudo ejecutar la vista previa de enrutamiento');
    }
  };

  const openIntentsDialog = async (channel: Channel) => {
    try {
      setLoadingIntents(true);
      const res = await axios.get(endpoints.agentflow.channels.intentsCatalog(TENANT_ID, channel.id));
      const items = (res.data?.items ?? []) as ChannelIntentCatalogItem[];
      setIntentCatalog(items);
      setSelectedIntentKeys(items.filter((item) => item.selected).map((item) => item.key));
      setIntentsChannel(channel);
      setOpenIntentsModal(true);
    } catch (err: any) {
      alert(err?.message || 'No se pudo cargar el catalogo de intenciones');
    } finally {
      setLoadingIntents(false);
    }
  };

  const saveChannelIntents = async () => {
    if (!intentsChannel) return;
    try {
      setSaving(true);
      await axios.post(endpoints.agentflow.channels.intentsApply(TENANT_ID, intentsChannel.id), {
        intentKeys: selectedIntentKeys,
      });
      setOpenIntentsModal(false);
      setIntentsChannel(null);
      setIntentCatalog([]);
      setSelectedIntentKeys([]);
      await fetchAll();
    } catch (err: any) {
      alert(getErrorMessage(err, 'No se pudieron aplicar las intenciones al canal'));
    } finally {
      setSaving(false);
    }
  };

  const fetchQrCode = async (channelId: string) => {
    const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}/qr`);
    return res.data?.qrCode as string | undefined;
  };

  const handleActivate = async (channel: Channel) => {
    try {
      await axios.post(`/api/v1/tenants/${TENANT_ID}/channels/${channel.id}/activate`);

      if (channel.type === 'WhatsApp' && (channel.config?.AuthMode || '').toLowerCase() === 'qr') {
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
      const qrSuffix = channel.type === 'WhatsApp' && (channel.config?.AuthMode || '').toLowerCase() === 'qr'
        ? ` | QR: ${res.data.healthy ? 'CONNECTED' : (res.data.qrAvailable ? 'AVAILABLE' : 'PENDING')}`
        : '';
      alert(`Estado: ${res.data.healthy ? 'OK' : 'NO DISPONIBLE'} - ${res.data.message || 'sin detalle'}${qrSuffix}`);
    } catch (err: any) {
      alert(err?.message || 'No se pudo validar el estado del canal');
    }
  };

  const openSessionEvidence = async (session: ChannelSession) => {
    try {
      setSelectedSession(session);
      setSessionLoading(true);
      const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channel-sessions/${session.id}/messages?page=0&pageSize=100`);
      setSessionMessages((res.data?.items ?? res.data ?? []) as SessionMessageEvidence[]);
    } catch (err: any) {
      alert(err?.message || 'No se pudieron cargar los mensajes de la sesion');
    } finally {
      setSessionLoading(false);
    }
  };

  const startFiniteQrPolling = async (channelId: string) => {
    setQrPolling(true);
    setQrPollRounds(0);

    const maxRounds = 30;
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
    if (channel.type === 'WebChat') return ['inbox', 'widget web'];
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
        <title>Canales de atencion | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <BrandPageHeader
          eyebrow="Canales"
          title="Canales de atencion"
          description="Prepara por donde entra o sale la conversacion. Un canal referencia una integracion reusable y luego se vincula con asistentes y automatizaciones."
          icon="mdi:access-point"
          help={<TermHelp title="Un canal es el medio por donde escribe o llama el cliente, por ejemplo WhatsApp, web chat, email o voz." />}
          actions={
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button
                variant="outlined"
                component={RouterLink}
                href={paths.dashboard.workflows}
                startIcon={<Iconify icon="mdi:source-branch" />}
              >
                Crear automatizacion
              </Button>
              <Button
                variant="outlined"
                component={RouterLink}
                href={paths.dashboard.agents}
                startIcon={<Iconify icon="mdi:robot-outline" />}
              >
                Crear asistente compatible
              </Button>
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
          }
        />

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={2.5}>
          <Grid item xs={12}>
            <Alert severity="info" sx={{ borderRadius: 2 }}>
              Uso recomendado: primero conectas el proveedor en <strong>Integraciones</strong>, luego creas el <strong>Canal</strong>, despues eliges el
              <strong> Asistente</strong> por defecto y finalmente enlazas la <strong>Automatizacion</strong> de entrada si hace falta.
            </Alert>
          </Grid>
          <Grid item xs={12}>
            <Grid container spacing={2}>
              {[
                ['Canales', channels.length, 'Configurados', 'mdi:message-processing-outline'],
                ['Activos', activeChannels, 'Listos para operar', 'mdi:check-decagram-outline'],
                ['WhatsApp', whatsappChannels, 'Templates y QR', 'mdi:whatsapp'],
                ['Conversaciones', sessions.length, 'Casos recientes', 'mdi:inbox-outline'],
              ].map(([label, value, helper, icon]) => (
                <Grid item xs={12} sm={6} md={3} key={label}>
                  <Card variant="outlined" sx={{ p: 2.25, height: '100%', borderRadius: 3 }}>
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
                          {label} | {helper}
                        </Typography>
                      </Box>
                    </Stack>
                  </Card>
                </Grid>
              ))}
            </Grid>
          </Grid>

          <Grid item xs={12}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3 }}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between">
                <Box>
                  <Typography variant="h6">Twilio omnicanal</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Una sola conexion Twilio se reutiliza para voz, centro de llamadas, SMS y futuros canales de WhatsApp por Twilio.
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

          <Grid item xs={12}>
            <Grid container spacing={2}>
              {[
                {
                  label: 'Integracion lista',
                  ready: twilioConnections.length > 0,
                  detail: twilioConnections.length > 0 ? 'Twilio conectado y reusable' : 'Falta conectar Twilio u otro proveedor',
                  cta: 'Configurar integracion',
                  href: paths.dashboard.marketplace,
                  action: undefined,
                },
                {
                  label: 'Canal listo',
                  ready: activeChannels > 0,
                  detail: activeChannels > 0 ? `${activeChannels} canal(es) activos` : 'No hay canales activos todavia',
                  cta: 'Agregar canal',
                  href: undefined,
                  action: () => setOpenCreate(true),
                },
                {
                  label: 'Asistente compatible',
                  ready: candidateAgents.length > 0,
                  detail: candidateAgents.length > 0 ? `${candidateAgents.length} asistentes disponibles` : 'Falta definir quien atiende',
                  cta: 'Crear asistente',
                  href: paths.dashboard.agents,
                  action: undefined,
                },
                {
                  label: 'Prueba ejecutada',
                  ready: sessions.length > 0,
                  detail: sessions.length > 0 ? 'Ya existen conversaciones o pruebas registradas' : 'Aun no hay prueba registrada',
                  cta: 'Probar canal',
                  href: undefined,
                  action: () => {
                    setTestPanelChannel((prev) => prev ?? channels[0] ?? null);
                    setOpenTestPanel(true);
                  },
                },
              ].map((item) => (
                <Grid item xs={12} md={3} key={item.label}>
                  <Card variant="outlined" sx={{ p: 2, borderRadius: 3, height: '100%' }}>
                    <Stack spacing={1}>
                      <Chip size="small" color={item.ready ? 'success' : 'warning'} label={item.ready ? 'OK' : 'Pendiente'} sx={{ alignSelf: 'flex-start' }} />
                      <Typography variant="subtitle2">{item.label}</Typography>
                      <Typography variant="caption" color="text.secondary">{item.detail}</Typography>
                      <Button
                        size="small"
                        variant={item.ready ? 'outlined' : 'contained'}
                        href={item.href}
                        onClick={item.action}
                      >
                        {item.cta}
                      </Button>
                    </Stack>
                  </Card>
                </Grid>
              ))}
            </Grid>
          </Grid>

          <Grid item xs={12} md={7}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, height: '100%' }}>
              <Typography variant="h6" sx={{ mb: 0.5 }}>Canales conectados</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Estos canales alimentan la bandeja y aparecen como entradas disponibles en automatizaciones.
              </Typography>
              {loading ? (
                <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
              ) : channels.length === 0 ? (
                <Alert severity="info">Aun no hay canales de atencion configurados.</Alert>
              ) : (
                <Box sx={{ overflowX: 'auto' }}>
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
                          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
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
                            <IconButton size="small" title="Cargar intenciones" onClick={() => openIntentsDialog(c)}>
                              <Iconify icon="mdi:playlist-check" />
                            </IconButton>
                            <IconButton size="small" title="Probar canal" onClick={() => { setTestPanelChannel(c); setTestResult(null); setTestMsg({ content: '', from: '', callbackUrl: '', asyncMode: false }); setOpenTestPanel(true); }}>
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
                </Box>
              )}
            </Card>
          </Grid>

          <Grid item xs={12} md={5}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, height: '100%' }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Conversaciones del canal</Typography>
              {sessions.length === 0 ? (
                <Alert severity="info">No hay conversaciones.</Alert>
              ) : (
                <>
                  <Box sx={{ overflowX: 'auto' }}>
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>Cliente</TableCell>
                          <TableCell>Canal</TableCell>
                          <TableCell>Estado</TableCell>
                          <TableCell>Ventana</TableCell>
                          <TableCell>Mensajes</TableCell>
                          <TableCell>Ultima actividad</TableCell>
                          <TableCell align="right">Accion</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {sessions.map((s) => (
                          <TableRow key={s.id} hover>
                            <TableCell>
                              <Typography variant="body2" fontWeight={700}>
                                {s.displayName || s.identifier}
                              </Typography>
                              <Typography variant="caption" color="text.secondary">
                                {s.displayName ? s.identifier : s.customerKind || 'unknown'}
                              </Typography>
                            </TableCell>
                            <TableCell>
                              <Chip label={s.channelType} size="small" />
                            </TableCell>
                            <TableCell>
                              <Chip label={s.status} size="small" color={getStatusColor(s.status) as any} />
                            </TableCell>
                            <TableCell>
                              <Chip
                                label={s.windowOpen ? 'Activa' : 'Cerrada'}
                                size="small"
                                color={s.windowOpen ? 'success' : 'default'}
                              />
                            </TableCell>
                            <TableCell>{s.messageCount}</TableCell>
                            <TableCell>{new Date(s.lastActivityAt).toLocaleString()}</TableCell>
                            <TableCell align="right">
                              <Button size="small" onClick={() => openSessionEvidence(s)}>
                                Ver detalle
                              </Button>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </Box>
                  <TablePagination
                    component="div"
                    count={sessionsTotal}
                    page={sessionsPage}
                    rowsPerPage={sessionsPageSize}
                    rowsPerPageOptions={[10, 25, 50]}
                    onPageChange={(_, nextPage) => setSessionsPage(nextPage)}
                    onRowsPerPageChange={(event) => {
                      setSessionsPageSize(Number(event.target.value));
                      setSessionsPage(0);
                    }}
                  />
                </>
              )}
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      {/* Create Channel Dialog */}
      <Dialog open={openCreate} onClose={() => setOpenCreate(false)} fullWidth maxWidth="sm">
        <DialogTitle sx={{ pb: 1 }}>
          <Typography variant="h6">Agregar canal de atencion</Typography>
          <Typography variant="body2" color="text.secondary">
            Define la via de comunicacion, el asistente por defecto y la integracion reusable que la soporta.
          </Typography>
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2.25} sx={{ pt: 1 }}>
            <Alert severity="info">
              Aqui defines la via de comunicacion. La logica vive en Automatizaciones y la conversacion vive en Asistentes.
            </Alert>
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
                  reutilizala para voz, call center, campanas y pasos de flujos automatizados.
                </Alert>
                <TextField
                  select
                  label="Integracion Twilio"
                  value={form.connectionId}
                  onChange={(e) => setForm((p) => ({ ...p, connectionId: e.target.value, provider: 'twilio' }))}
                  fullWidth
                  helperText={connections.length === 0 ? 'No hay integraciones creadas. Ve a Integraciones para crear Twilio.' : 'Se reutiliza para todos los canales de voz.'}
                >
                  <MenuItem value="">Detectar por proveedor Twilio</MenuItem>
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
              label="Asistente por defecto"
              value={form.defaultAgentId}
              onChange={(e) => setForm((p) => ({ ...p, defaultAgentId: e.target.value }))}
              fullWidth
              helperText={candidateAgents.length === 0 ? 'No hay asistentes disponibles' : 'Define quien atiende por defecto cuando no aplica ninguna regla especial.'}
            >
              <MenuItem value=""><em>Sin asistente por defecto</em></MenuItem>
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>
                  {agent.name}
                </MenuItem>
              ))}
            </TextField>

            <Divider textAlign="left" sx={{ pt: 0.5 }}>
              <Typography variant="caption" color="text.secondary">Decision de entrada (opcional)</Typography>
            </Divider>

            <FormControl fullWidth>
              <InputLabel>Agentes de intencion</InputLabel>
              <Select
                multiple
                value={form.routingAgentIds}
                onChange={(e) => setForm((p) => ({ ...p, routingAgentIds: e.target.value as string[] }))}
                input={<OutlinedInput label="Agentes de intencion" />}
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
              <FormHelperText>El sistema repartira nuevas conversaciones entre estos asistentes segun su carga actual. Dejalo vacio para usar solo el asistente por defecto.</FormHelperText>
            </FormControl>

            {form.type === 'WhatsApp' && (
              <>
                <TextField
                  label="Ventana de conversacion (horas)"
                  type="number"
                  value={form.sessionWindowHours}
                  onChange={(e) => setForm((p) => ({ ...p, sessionWindowHours: e.target.value }))}
                  fullWidth
                  helperText="Horas en que la conversacion de WhatsApp sigue abierta para responder sin reactivar el caso. Predeterminado: 24."
                  inputProps={{ min: 1, max: 168 }}
                />
                <TextField
                  select
                  label="Agente enrutador"
                  value={form.routerAgentId}
                  onChange={(e) => setForm((p) => ({ ...p, routerAgentId: e.target.value }))}
                  fullWidth
                  helperText="Recibe primero, interpreta la necesidad del cliente y decide que flujo o responsable debe continuar."
                >
                  <MenuItem value="">Sin enrutador (usa agente de respaldo)</MenuItem>
                  {candidateAgents.map((agent) => (
                    <MenuItem key={agent.id} value={agent.id}>
                      {agent.name}
                    </MenuItem>
                  ))}
                </TextField>
                <TextField
                  label="Mensaje aprobado para reabrir (WhatsApp)"
                  value={form.reopenTemplateName}
                  onChange={(e) => setForm((p) => ({ ...p, reopenTemplateName: e.target.value }))}
                  fullWidth
                  helperText="Nombre del mensaje aprobado por WhatsApp para retomar una conversacion vencida. Ejemplo: session_reopen"
                />
              </>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenCreate(false)}>Cancelar</Button>
          <Button variant="contained" onClick={handleCreate} disabled={saving || !form.name}>
            {saving ? 'Agregando...' : 'Guardar canal'}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={openRouting} onClose={() => setOpenRouting(false)} fullWidth maxWidth="sm">
        <DialogTitle sx={{ pb: 1 }}>
          <Typography variant="h6">Enrutamiento por intencion</Typography>
          <Typography variant="body2" color="text.secondary">
            {routingChannel?.name ? `Canal: ${routingChannel.name}` : 'Configura como se reparte la conversacion.'}
          </Typography>
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            <Alert severity="info" sx={{ mb: 0 }}>
              El <strong>agente enrutador</strong> clasifica el mensaje. Los <strong>agentes por intencion</strong> reciben nuevas conversaciones segun su carga.
            </Alert>

            <TextField
              select
              label="Agente de respaldo"
              value={routingForm.defaultAgentId}
              onChange={(e) => setRoutingForm((prev) => ({ ...prev, defaultAgentId: e.target.value }))}
              fullWidth
              helperText="Se usa cuando no hay una intencion clara o no hay agente de intencion disponible."
            >
              <MenuItem value=""><em>Sin agente de respaldo</em></MenuItem>
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
              ))}
            </TextField>

            <FormControl fullWidth>
              <InputLabel>Agentes de intencion</InputLabel>
              <Select
                multiple
                value={routingForm.routingAgentIds}
                onChange={(e) => setRoutingForm((prev) => ({ ...prev, routingAgentIds: e.target.value as string[] }))}
                input={<OutlinedInput label="Agentes de intencion" />}
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
              <FormHelperText>El router enviara la conversacion al agente de intencion con menor carga activa.</FormHelperText>
            </FormControl>

            <Divider />

            <FormControl fullWidth>
              <InputLabel>Estrategia sin match</InputLabel>
              <Select
                value={routingForm.noMatchAction}
                onChange={(e) => setRoutingForm((prev) => ({ ...prev, noMatchAction: e.target.value }))}
                input={<OutlinedInput label="Estrategia sin match" />}
              >
                <MenuItem value="human_review_only">Solo revision humana</MenuItem>
                <MenuItem value="clarify_then_route">Preguntar y reintentar</MenuItem>
              </Select>
            </FormControl>

            <TextField
              select
              label="Agente de respaldo"
              value={routingForm.routerFallbackAgentId}
              onChange={(e) => setRoutingForm((prev) => ({ ...prev, routerFallbackAgentId: e.target.value }))}
              fullWidth
              helperText="Agente responsable del proceso de respaldo y trazabilidad de auditoria."
            >
              <MenuItem value=""><em>Sin agente de respaldo</em></MenuItem>
              {candidateAgents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
              ))}
            </TextField>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                label="Maximo de preguntas"
                type="number"
                value={routingForm.maxClarificationTurns}
                onChange={(e) => setRoutingForm((prev) => ({ ...prev, maxClarificationTurns: Number(e.target.value || 2) }))}
                inputProps={{ min: 1, max: 5 }}
                fullWidth
                disabled={routingForm.noMatchAction !== 'clarify_then_route'}
              />
              <TextField
                select
                label="Destino de escalacion"
                value={routingForm.escalationTarget}
                onChange={(e) => setRoutingForm((prev) => ({ ...prev, escalationTarget: e.target.value }))}
                fullWidth
                helperText="Cola humana que recibira casos escalados sin clasificacion."
              >
                <MenuItem value=""><em>Sin cola de escalacion</em></MenuItem>
                {queueOptions.filter((q) => q.active).map((q) => (
                  <MenuItem key={q.id} value={q.id}>{q.name}</MenuItem>
                ))}
              </TextField>
            </Stack>

            <Stack spacing={1.25}>
              <Typography variant="subtitle2">Preguntas de clarificacion</Typography>
              {routingForm.clarificationQuestions.map((q, idx) => (
                <Stack key={`q-${idx}`} direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                  <TextField
                    label={`Pregunta ${idx + 1}`}
                    value={q.text}
                    onChange={(e) => setRoutingForm((prev) => ({
                      ...prev,
                      clarificationQuestions: prev.clarificationQuestions.map((item, i) => i === idx ? { ...item, text: e.target.value } : item),
                    }))}
                    fullWidth
                    disabled={routingForm.noMatchAction !== 'clarify_then_route'}
                  />
                  <TextField
                    label="Campo"
                    value={q.field}
                    onChange={(e) => setRoutingForm((prev) => ({
                      ...prev,
                      clarificationQuestions: prev.clarificationQuestions.map((item, i) => i === idx ? { ...item, field: e.target.value } : item),
                    }))}
                    sx={{ minWidth: 140 }}
                    disabled={routingForm.noMatchAction !== 'clarify_then_route'}
                  />
                  <FormControl sx={{ minWidth: 92 }}>
                    <InputLabel>Activa</InputLabel>
                    <Select
                      value={q.active ? 'si' : 'no'}
                      label="Activa"
                      onChange={(e) => setRoutingForm((prev) => ({
                        ...prev,
                        clarificationQuestions: prev.clarificationQuestions.map((item, i) => i === idx ? { ...item, active: e.target.value === 'si' } : item),
                      }))}
                      disabled={routingForm.noMatchAction !== 'clarify_then_route'}
                    >
                      <MenuItem value="si">Si</MenuItem>
                      <MenuItem value="no">No</MenuItem>
                    </Select>
                  </FormControl>
                </Stack>
              ))}
            </Stack>

            {routingPreview && (
              <Alert severity="success">
                <strong>Proximo responsable:</strong> {candidateAgents.find(a => a.id === routingPreview.suggestedAgentId)?.name || routingPreview.suggestedAgentId || 'N/A'}
                {Object.keys(routingPreview.activeLoadByAgent || {}).length > 0 && (
                  <Typography variant="caption" display="block" sx={{ mt: 0.5 }}>
                    Carga actual: {Object.entries(routingPreview.activeLoadByAgent || {}).map(([a, l]) => {
                      const name = candidateAgents.find(ca => ca.id === a)?.name || a;
                      return `${name}: ${l}`;
                    }).join(' | ')}
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

      <Dialog open={openIntentsModal} onClose={() => setOpenIntentsModal(false)} fullWidth maxWidth="md">
        <DialogTitle sx={{ pb: 1 }}>
          <Typography variant="h6">Cargar intenciones</Typography>
          <Typography variant="body2" color="text.secondary">
            {intentsChannel?.name ? `Canal: ${intentsChannel.name}` : 'Selecciona las intenciones que este canal puede clasificar.'}
          </Typography>
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Alert severity="info">
              Selecciona las intenciones que este canal debe usar para clasificar y enrutar mensajes. Puedes quitar o agregar antes de cargar.
            </Alert>
            {loadingIntents ? (
              <Box sx={{ py: 4, textAlign: 'center' }}>
                <CircularProgress size={24} />
              </Box>
            ) : (
              <>
                <Stack direction="row" spacing={1} alignItems="center">
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => setSelectedIntentKeys(intentCatalog.map((item) => item.key))}
                  >
                    Seleccionar todo
                  </Button>
                  <Button
                    size="small"
                    variant="text"
                    onClick={() => setSelectedIntentKeys([])}
                  >
                    Limpiar
                  </Button>
                  <Chip size="small" label={`${selectedIntentKeys.length} seleccionadas`} />
                </Stack>
                <Box sx={{ overflowX: 'auto' }}>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell padding="checkbox" />
                        <TableCell>Intencion</TableCell>
                        <TableCell>Categoria</TableCell>
                        <TableCell>Descripcion</TableCell>
                        <TableCell>Prioridad</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {intentCatalog.map((item) => {
                        const checked = selectedIntentKeys.includes(item.key);
                        return (
                          <TableRow
                            key={item.key}
                            hover
                            onClick={() => {
                              setSelectedIntentKeys((prev) =>
                                prev.includes(item.key) ? prev.filter((key) => key !== item.key) : [...prev, item.key]
                              );
                            }}
                            sx={{ cursor: 'pointer' }}
                          >
                            <TableCell padding="checkbox">
                              <Checkbox
                                checked={checked}
                                onClick={(e) => e.stopPropagation()}
                                onChange={() => {
                                  setSelectedIntentKeys((prev) =>
                                    prev.includes(item.key) ? prev.filter((key) => key !== item.key) : [...prev, item.key]
                                  );
                                }}
                              />
                            </TableCell>
                            <TableCell>
                              <Stack spacing={0.25}>
                                <Typography variant="body2">{item.name}</Typography>
                                <Typography variant="caption" color="text.secondary">{item.key}</Typography>
                              </Stack>
                            </TableCell>
                            <TableCell>{item.category}</TableCell>
                            <TableCell sx={{ maxWidth: 420 }}>
                              <Typography variant="body2" noWrap>{item.description}</Typography>
                            </TableCell>
                            <TableCell>{item.priority}</TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </Box>
              </>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenIntentsModal(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveChannelIntents} disabled={saving || loadingIntents}>
            {saving ? 'Cargando...' : 'Cargar intenciones'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* QR Code Dialog */}
      <Dialog
        open={Boolean(selectedChannel && (qrPolling || qrCode))}
        onClose={() => {
          setQrCode(null);
          setSelectedChannel(null);
          setQrPolling(false);
          setQrPollRounds(0);
        }}
        maxWidth="sm"
      >
        <DialogTitle sx={{ pb: 1 }}>
          <Typography variant="h6">Escanear codigo QR</Typography>
          <Typography variant="body2" color="text.secondary">
            {selectedChannel?.name ? `Canal: ${selectedChannel.name}` : 'Vincula el canal de WhatsApp desde el telefono.'}
          </Typography>
        </DialogTitle>
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
                Esperando confirmacion de conexion... intento {qrPollRounds}/30
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
                  setError('El QR aun no esta disponible. Revisa la conexion del bridge e intenta de nuevo.');
                }
              } catch {
                setError('El QR aun no esta disponible. Revisa la conexion del bridge e intenta de nuevo.');
              }
            }}
            disabled={qrPolling}
          >
            Refrescar QR
          </Button>
          <Button
            onClick={() => {
              setQrCode(null);
              setSelectedChannel(null);
              setQrPolling(false);
              setQrPollRounds(0);
            }}
          >
            Cerrar
          </Button>
        </DialogActions>
      </Dialog>

      {/* Session Evidence Dialog */}
      <Dialog open={!!selectedSession} onClose={() => setSelectedSession(null)} fullWidth maxWidth="md">
        <DialogTitle sx={{ pb: 1 }}>
          <Typography variant="h6">Historial de la conversacion</Typography>
          <Typography variant="body2" color="text.secondary">
            {selectedSession?.identifier ? `Cliente o sesion: ${selectedSession.identifier}` : 'Revisa el detalle completo de la conversacion.'}
          </Typography>
        </DialogTitle>
        <DialogContent>
          {sessionLoading ? (
            <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
          ) : (
            <Stack spacing={1.5}>
              <Alert severity="info">
                ID de ejecucion: {firstExecutionId || 'N/A'} | Mensaje entrante: {messageIdIn || 'N/A'} | Mensaje saliente: {messageIdOut || 'N/A'} | Tiempo total: {latencyMs} ms
              </Alert>
              {sessionMessages.length === 0 ? (
                <Alert severity="warning">No se encontraron mensajes para esta conversacion.</Alert>
              ) : (
                <Box sx={{ overflowX: 'auto' }}>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Direccion</TableCell>
                        <TableCell>Actor</TableCell>
                        <TableCell>Contenido</TableCell>
                        <TableCell>Estado</TableCell>
                        <TableCell>Entrega</TableCell>
                        <TableCell>Creado</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {sessionMessages.map((m) => (
                        <TableRow key={m.id}>
                          <TableCell>{m.direction}</TableCell>
                          <TableCell>{m.actor || '-'}</TableCell>
                          <TableCell sx={{ maxWidth: 420, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                            {m.errorMessage || m.content}
                          </TableCell>
                          <TableCell>{m.status}</TableCell>
                          <TableCell>{m.deliveryState || '-'}</TableCell>
                          <TableCell>{new Date(m.createdAt).toLocaleString()}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Box>
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
        <DialogTitle sx={{ pb: 1 }}>
          <Typography variant="h6">Probar mensaje</Typography>
          <Typography variant="body2" color="text.secondary">
            {testPanelChannel?.name ? `Canal: ${testPanelChannel.name}` : 'Valida la entrada del cliente antes de publicar.'}
          </Typography>
        </DialogTitle>
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
                label={testMsg.asyncMode ? 'Modo: Asincrono (202)' : 'Modo: Sincrono (200)'}
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










