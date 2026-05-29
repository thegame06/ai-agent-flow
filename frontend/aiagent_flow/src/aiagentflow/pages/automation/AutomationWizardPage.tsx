import { Helmet } from 'react-helmet-async';
import { useSearchParams } from 'react-router';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardHeader from '@mui/material/CardHeader';
import CardContent from '@mui/material/CardContent';
import { alpha, useTheme } from '@mui/material/styles';
import CircularProgress from '@mui/material/CircularProgress';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { MessageTimeline } from 'src/aiagentflow/pages/threads/components/MessageTimeline';
import { type WizardId, WizardLauncher } from 'src/aiagentflow/components/chat-wizard/WizardRegistry';

type ViewMode = 'focus' | 'history';

type AgentRow = {
  id: string;
  name?: string;
  isSystemAgent?: boolean;
  systemRole?: string;
};

type ThreadSummary = {
  threadId: string;
  threadKey?: string;
  turnCount?: number;
  updatedAt?: string;
};

type ThreadTurn = {
  userMessage?: string;
  assistantResponse?: string;
  timestamp?: string;
};

type TimelineMessage = {
  id: string;
  direction: string;
  content: string;
  createdAt: string;
  actor?: string;
  deliveryState?: string;
  errorMessage?: string;
  metadata?: Record<string, string>;
};

export default function AutomationWizardPage() {
  const theme = useTheme();
  const tenantId = useTenantId();
  const [searchParams] = useSearchParams();
  const channelId = searchParams.get('channelId') ?? undefined;
  const wizardParam = searchParams.get('wizard');
  const [viewMode, setViewMode] = useState<ViewMode>('focus');
  const [wizardId, setWizardId] = useState<WizardId>(
    wizardParam === 'agentSubflow'
      ? 'agentSubflow'
      : wizardParam === 'outboundVoice'
        ? 'outboundVoice'
        : 'automation'
  );
  const [configAssistantId, setConfigAssistantId] = useState('');
  const [threads, setThreads] = useState<ThreadSummary[]>([]);
  const [query, setQuery] = useState('');
  const [selectedThreadId, setSelectedThreadId] = useState('');
  const [messages, setMessages] = useState<TimelineMessage[]>([]);
  const [loading, setLoading] = useState(false);
  const [threadLoading, setThreadLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    const loadConfigAssistant = async () => {
      try {
        const res = await axios.get(endpoints.agentflow.agents.list(tenantId));
        const agents = (res.data ?? []) as AgentRow[];
        const configAgent =
          agents.find((a) => a.isSystemAgent && a.systemRole === 'ConfigAssistant') ??
          agents.find((a) => (a.name || '').toLowerCase().includes('config assistant')) ??
          null;
        setConfigAssistantId(configAgent?.id ?? '');
      } catch {
        setConfigAssistantId('');
      }
    };
    loadConfigAssistant();
  }, [tenantId]);

  useEffect(() => {
    const loadThreads = async () => {
      if (viewMode !== 'history') return;
      try {
        setLoading(true);
        setError('');
        const suffix = configAssistantId ? `?agentId=${encodeURIComponent(configAssistantId)}&limit=100` : '?limit=100';
        const res = await axios.get(`/api/v1/tenants/${tenantId}/threads${suffix}`);
        const next = (res.data ?? []) as ThreadSummary[];
        setThreads(next);
        setSelectedThreadId((prev) => (next.some((t) => t.threadId === prev) ? prev : next[0]?.threadId ?? ''));
      } catch (e: any) {
        setError(e?.message ?? 'No se pudo cargar el historial');
      } finally {
        setLoading(false);
      }
    };
    loadThreads();
  }, [tenantId, configAssistantId, viewMode]);

  const filteredThreads = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return threads;
    return threads.filter((t) => `${t.threadKey || ''} ${t.threadId}`.toLowerCase().includes(q));
  }, [threads, query]);

  const selectedThread = useMemo(
    () => threads.find((t) => t.threadId === selectedThreadId) ?? filteredThreads[0] ?? null,
    [threads, filteredThreads, selectedThreadId]
  );

  useEffect(() => {
    const loadThread = async () => {
      if (viewMode !== 'history' || !selectedThread) {
        setMessages([]);
        return;
      }
      try {
        setThreadLoading(true);
        const res = await axios.get(`/api/v1/tenants/${tenantId}/threads/${selectedThread.threadId}/history?limit=200`);
        const turns = (res.data?.turns ?? []) as ThreadTurn[];
        const mapped: TimelineMessage[] = [];
        turns.forEach((turn, idx) => {
          const baseTs = turn.timestamp || new Date().toISOString();
          if (turn.userMessage) {
            mapped.push({
              id: `u-${idx}`,
              direction: 'Incoming',
              content: turn.userMessage,
              createdAt: baseTs,
              actor: 'customer',
            });
          }
          if (turn.assistantResponse) {
            mapped.push({
              id: `a-${idx}`,
              direction: 'Outgoing',
              content: turn.assistantResponse,
              createdAt: baseTs,
              actor: 'bot',
            });
          }
        });
        setMessages(mapped);
      } catch (e: any) {
        setError(e?.message ?? 'No se pudo cargar el detalle');
      } finally {
        setThreadLoading(false);
      }
    };
    loadThread();
  }, [tenantId, selectedThread, viewMode]);

  return (
    <>
      <Helmet>
        <title>Crear automatizacion | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="lg">
        <Stack spacing={2}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Typography variant="h4">Crear automatizacion</Typography>
            <Stack direction="row" spacing={1}>
              <Button variant={viewMode === 'focus' ? 'contained' : 'outlined'} onClick={() => setViewMode('focus')}>
                Enfoque
              </Button>
              <Button variant={viewMode === 'history' ? 'contained' : 'outlined'} onClick={() => setViewMode('history')}>
                Historial
              </Button>
            </Stack>
          </Stack>

          {viewMode === 'focus' ? (
            <Stack spacing={2}>
              <Typography variant="body2" color="text.secondary">
                Asistente guiado reusable para crear automatizaciones. Tambien puede incrustarse en Inicio y otros modulos.
              </Typography>
              <WizardLauncher value={wizardId} onChange={setWizardId} initialChannelId={channelId} />
            </Stack>
          ) : (
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '340px minmax(0,1fr)' }, gap: 1.5 }}>
              <Card
                variant="outlined"
                sx={{
                  bgcolor: theme.palette.mode === 'dark' ? alpha(theme.palette.background.paper, 0.9) : 'background.paper',
                  borderColor: theme.palette.mode === 'dark' ? alpha(theme.palette.common.white, 0.1) : 'divider',
                }}
              >
                <CardHeader title={`Conversaciones ${threads.length}`} />
                <CardContent sx={{ pt: 0 }}>
                  <TextField
                    fullWidth
                    size="small"
                    placeholder="Buscar conversaciones"
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                  />
                  <Stack spacing={1} sx={{ mt: 1.5, maxHeight: '65vh', overflow: 'auto' }}>
                    {loading ? <CircularProgress size={20} /> : filteredThreads.map((thread) => (
                      <Box
                        key={thread.threadId}
                        onClick={() => setSelectedThreadId(thread.threadId)}
                        sx={{
                          border: '1px solid',
                          borderColor: selectedThread?.threadId === thread.threadId ? 'primary.main' : 'divider',
                          borderRadius: 1.25,
                          p: 1.1,
                          cursor: 'pointer',
                          bgcolor:
                            selectedThread?.threadId === thread.threadId
                              ? 'action.selected'
                              : theme.palette.mode === 'dark'
                                ? alpha(theme.palette.background.paper, 0.86)
                                : 'background.paper',
                        }}
                      >
                        <Typography variant="subtitle2" noWrap>{thread.threadKey || thread.threadId.slice(0, 14)}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {thread.turnCount ?? 0} turnos
                        </Typography>
                      </Box>
                    ))}
                  </Stack>
                </CardContent>
              </Card>

              <Card
                variant="outlined"
                sx={{
                  bgcolor: theme.palette.mode === 'dark' ? alpha(theme.palette.background.paper, 0.9) : 'background.paper',
                  borderColor: theme.palette.mode === 'dark' ? alpha(theme.palette.common.white, 0.1) : 'divider',
                }}
              >
                <CardHeader
                  title="Detalle de conversacion"
                  subheader={selectedThread ? `Hilo ${selectedThread.threadKey || selectedThread.threadId.slice(0, 10)}` : 'Selecciona una conversacion'}
                />
                <CardContent>
                  {error ? <Typography color="error" variant="body2" sx={{ mb: 1 }}>{error}</Typography> : null}
                  <MessageTimeline
                    messages={messages}
                    loading={threadLoading}
                    hasMore={false}
                    onLoadMore={() => {}}
                  />
                </CardContent>
              </Card>
            </Box>
          )}
        </Stack>
      </DashboardContent>
    </>
  );
}

