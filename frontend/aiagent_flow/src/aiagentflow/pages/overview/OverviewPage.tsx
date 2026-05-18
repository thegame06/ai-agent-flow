import type { KeyboardEvent } from 'react';
import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { Helmet } from 'react-helmet-async';
import { useRef, useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { fetchOverview } from './overviewSlice';

type SystemOrchestratorStatus = {
  gaps?: string[];
  workflows?: unknown[];
  channels?: unknown[];
  connections?: Array<{ ready: boolean }>;
};

type ChatMessage = { role: 'user' | 'assistant'; content: string };

export default function OverviewPage() {
  const dispatch = useDispatch<AppDispatch>();
  const tenantId = useTenantId();
  const theme = useTheme();
  const inputRef = useRef<HTMLInputElement>(null);

  const [assistantPrompt, setAssistantPrompt] = useState('');
  const [orchestratorStatus, setOrchestratorStatus] = useState<SystemOrchestratorStatus | null>(null);
  const [configAssistantId, setConfigAssistantId] = useState<string | null>(null);
  const [chatThreadId, setChatThreadId] = useState<string | null>(null);
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([]);
  const [chatLoading, setChatLoading] = useState(false);

  const { metrics, loading } = useSelector((state: RootState) => state.overview);

  useEffect(() => {
    dispatch(fetchOverview(tenantId));
  }, [dispatch, tenantId]);

  // Fetch orchestrator status + find ConfigAssistant agent
  useEffect(() => {
    let active = true;

    axios
      .get(endpoints.agentflow.systemOrchestrator.status(tenantId))
      .then((res) => { if (active) setOrchestratorStatus(res.data as SystemOrchestratorStatus); })
      .catch(() => { if (active) setOrchestratorStatus(null); });

    axios
      .get(endpoints.agentflow.agents.list(tenantId))
      .then((res) => {
        if (!active) return;
        const agents: any[] = res.data ?? [];
        const ca = agents.find((a) => a.systemRole === 'ConfigAssistant' || a.tags?.includes('config-assistant'));
        if (ca) setConfigAssistantId(ca.id);
      })
      .catch(() => {});

    return () => { active = false; };
  }, [tenantId]);

  const sendMessage = async () => {
    const msg = assistantPrompt.trim();
    if (!msg || chatLoading) return;

    setAssistantPrompt('');
    setChatMessages((prev) => [...prev, { role: 'user', content: msg }]);
    setChatLoading(true);

    try {
      let threadId = chatThreadId;

      // Lazy-create thread on first message
      if (!threadId) {
        if (!configAssistantId) {
          setChatMessages((prev) => [
            ...prev,
            { role: 'assistant', content: 'El asistente de configuracion no esta disponible todavia. Verifica que la configuracion inicial se haya creado correctamente.' },
          ]);
          setChatLoading(false);
          return;
        }
        const threadRes = await axios.post(endpoints.agentflow.threads.create(tenantId), {
          agentId: configAssistantId,
        });
        threadId = threadRes.data?.threadId ?? threadRes.data?.id;
        setChatThreadId(threadId);
      }

      const msgRes = await axios.post(
        endpoints.agentflow.threads.sendMessage(tenantId, threadId!),
        { message: msg }
      );

      const reply: string = msgRes.data?.assistantResponse ?? msgRes.data?.response ?? '(sin respuesta)';
      setChatMessages((prev) => [...prev, { role: 'assistant', content: reply }]);
    } catch (err: any) {
      setChatMessages((prev) => [
        ...prev,
        { role: 'assistant', content: `Error: ${err?.response?.data?.message ?? err?.message ?? 'Error desconocido'}` },
      ]);
    } finally {
      setChatLoading(false);
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
  };

  const workflowCount = orchestratorStatus?.workflows?.length ?? 0;
  const channelCount = orchestratorStatus?.channels?.length ?? 0;
  const readyConnections = orchestratorStatus?.connections?.filter((c) => c.ready).length ?? 0;
  const isReady = workflowCount > 0 && channelCount > 0;

  const quickLinks = [
    { label: 'Flujos automatizados', icon: 'mdi:source-branch', href: paths.dashboard.workflows },
    { label: 'Canales', icon: 'mdi:message-processing-outline', href: paths.dashboard.system.channels },
    { label: 'Integraciones', icon: 'mdi:connection', href: paths.dashboard.marketplace },
    { label: 'Asistentes IA', icon: 'mdi:robot-outline', href: paths.dashboard.agents },
  ];

  return (
    <>
      <Helmet>
        <title>Inicio | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="md">
        {loading && <LinearProgress sx={{ mb: 2 }} />}

        {/* ── Hero + Assistant ── */}
        <Paper
          variant="outlined"
          sx={{
            p: { xs: 3, md: 4 },
            borderRadius: 4,
            overflow: 'hidden',
            borderColor: alpha(theme.palette.primary.main, 0.16),
            background:
              'radial-gradient(circle at 6% 18%, rgba(14,124,90,0.18), transparent 28%), radial-gradient(circle at 94% 0%, rgba(0,167,181,0.18), transparent 26%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          {/* Header */}
          <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2.5 }}>
            <Avatar
              src="/logo/logo-single.svg"
              alt="Annonai"
              sx={{ width: 52, height: 52, bgcolor: 'transparent', boxShadow: `0 12px 32px ${alpha(theme.palette.primary.main, 0.22)}` }}
            />
            <Box sx={{ flex: 1 }}>
              <Typography variant="h4" sx={{ fontWeight: 800, letterSpacing: -0.6, lineHeight: 1.2 }}>
                Hola, soy tu asistente
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Pregúntame qué quieres automatizar y te guío al lugar correcto.
              </Typography>
            </Box>
            <Chip
              size="small"
              icon={<Iconify icon={isReady ? 'mdi:check-circle' : 'mdi:clock-outline'} width={14} />}
              label={isReady ? 'Listo para operar' : 'Configuración pendiente'}
              color={isReady ? 'success' : 'warning'}
              variant="soft"
            />
          </Stack>

          {/* Status row */}
          <Stack direction="row" spacing={1} sx={{ mb: 3 }} flexWrap="wrap">
            {[
              { label: `${workflowCount} flujo${workflowCount !== 1 ? 's' : ''}`, icon: 'mdi:source-branch' },
              { label: `${channelCount} canal${channelCount !== 1 ? 'es' : ''}`, icon: 'mdi:chat-processing-outline' },
              { label: `${readyConnections} integración${readyConnections !== 1 ? 'es' : ''}`, icon: 'mdi:connection' },
              { label: `${metrics.publishedAgents} asistente${metrics.publishedAgents !== 1 ? 's' : ''} activos`, icon: 'mdi:robot-outline' },
            ].map((item) => (
              <Chip
                key={item.label}
                size="small"
                icon={<Iconify icon={item.icon} width={14} />}
                label={item.label}
                variant="outlined"
                sx={{ bgcolor: alpha(theme.palette.background.paper, 0.7) }}
              />
            ))}
          </Stack>

          <Divider sx={{ mb: 3 }} />

          {/* Chat area */}
          <Stack spacing={1.5} sx={{ mb: 2 }}>
            {chatMessages.length === 0 && (
              <Typography variant="body2" color="text.disabled" sx={{ fontStyle: 'italic', textAlign: 'center', py: 1 }}>
                Escribe tu primera pregunta y el asistente de configuración te ayudará.
              </Typography>
            )}
            {chatMessages.map((m, i) => (
              <Box
                key={i}
                sx={{
                  p: 1.5,
                  borderRadius: 2,
                  maxWidth: '88%',
                  alignSelf: m.role === 'user' ? 'flex-end' : 'flex-start',
                  ml: m.role === 'user' ? 'auto' : 0,
                  bgcolor: m.role === 'user'
                    ? alpha(theme.palette.primary.main, 0.12)
                    : alpha(theme.palette.background.paper, 0.9),
                  border: `1px solid ${alpha(m.role === 'user' ? theme.palette.primary.main : theme.palette.grey[300], 0.3)}`,
                }}
              >
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                  {m.content}
                </Typography>
              </Box>
            ))}
            {chatLoading && (
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'text.secondary', pl: 0.5 }}>
                <CircularProgress size={14} />
                <Typography variant="caption">El asistente está pensando...</Typography>
              </Box>
            )}
          </Stack>

          {/* Input */}
          <Stack direction="row" spacing={1}>
            <TextField
              inputRef={inputRef}
              fullWidth
              size="small"
              value={assistantPrompt}
              onChange={(e) => setAssistantPrompt(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Ejemplo: quiero atender WhatsApp y agendar citas automáticamente..."
              disabled={chatLoading}
              sx={{ bgcolor: alpha(theme.palette.background.paper, 0.8), borderRadius: 2 }}
            />
            <IconButton
              color="primary"
              onClick={sendMessage}
              disabled={chatLoading || !assistantPrompt.trim()}
              sx={{ bgcolor: 'primary.main', color: 'white', '&:hover': { bgcolor: 'primary.dark' }, '&:disabled': { bgcolor: 'action.disabledBackground' } }}
            >
              {chatLoading ? <CircularProgress size={18} sx={{ color: 'white' }} /> : <Iconify icon="mdi:send" width={20} />}
            </IconButton>
          </Stack>

          {!configAssistantId && (
            <Typography variant="caption" color="text.disabled" sx={{ mt: 1, display: 'block' }}>
              Asistente no disponible. La configuracion inicial todavia no creo el asistente base.
            </Typography>
          )}
        </Paper>

        {/* ── Quick links ── */}
        <Stack direction="row" spacing={1} sx={{ mt: 2.5 }} flexWrap="wrap">
          {quickLinks.map((link) => (
            <Button
              key={link.label}
              component={RouterLink}
              href={link.href}
              variant="outlined"
              size="small"
              startIcon={<Iconify icon={link.icon} width={16} />}
              sx={{ borderRadius: 6, color: 'text.secondary', borderColor: 'divider', '&:hover': { borderColor: 'primary.main', color: 'primary.main' } }}
            >
              {link.label}
            </Button>
          ))}
          <Box sx={{ flex: 1 }} />
          <Chip
            size="small"
            label={`${metrics.completedToday} ejecuciones hoy · ${Math.round(metrics.avgQualityScore * 100)}% calidad`}
            variant="outlined"
            sx={{ color: 'text.secondary' }}
          />
        </Stack>
      </DashboardContent>
    </>
  );
}
