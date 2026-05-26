import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import CardHeader from '@mui/material/CardHeader';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';
import { useRouter } from 'src/routes/hooks';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { useAgents } from './Hooks/useAgents';

type ViewMode = 'classic' | 'list-detail';

type ThreadSummary = {
  threadId: string;
  threadKey: string;
  turnCount: number;
  createdAt?: string;
  updatedAt?: string;
};

type ThreadTurn = {
  userMessage?: string;
  assistantResponse?: string;
  timestamp?: string;
};

export default function AgentsPage() {
  const tenantId = useTenantId();
  const router = useRouter();
  const { agents, loading } = useAgents(tenantId, null);
  const [viewMode, setViewMode] = useState<ViewMode>('list-detail');

  return (
    <>
      <Helmet>
        <title>Assistants | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth={false}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1.5 }}>
          <Typography variant="h4">Assistants</Typography>
          <Stack direction="row" spacing={1}>
            <Button variant={viewMode === 'classic' ? 'contained' : 'outlined'} onClick={() => setViewMode('classic')}>
              Vista actual
            </Button>
            <Button variant={viewMode === 'list-detail' ? 'contained' : 'outlined'} onClick={() => setViewMode('list-detail')}>
              Lista detalle
            </Button>
          </Stack>
        </Stack>

        {viewMode === 'classic' ? (
          <ClassicAssistantsView
            loading={loading}
            agents={agents}
            onEdit={(id) => router.push(paths.dashboard.agentEdit(id))}
            onChat={(id) => router.push(`${paths.dashboard.agents}/${id}/chat`)}
            onView={(id) => router.push(`${paths.dashboard.agents}/${id}`)}
            onCreate={() => router.push(paths.dashboard.agentDesigner)}
          />
        ) : (
          <ListDetailAssistantsView
            tenantId={tenantId}
            loading={loading}
            agents={agents}
            onEdit={(id) => router.push(paths.dashboard.agentEdit(id))}
            onChat={(id) => router.push(`${paths.dashboard.agents}/${id}/chat`)}
            onCreate={() => router.push(paths.dashboard.agentDesigner)}
          />
        )}
      </DashboardContent>
    </>
  );
}

function ClassicAssistantsView({
  loading,
  agents,
  onEdit,
  onChat,
  onView,
  onCreate,
}: {
  loading: boolean;
  agents: any[];
  onEdit: (id: string) => void;
  onChat: (id: string) => void;
  onView: (id: string) => void;
  onCreate: () => void;
}) {
  if (loading) {
    return (
      <Stack alignItems="center" sx={{ py: 6 }}>
        <CircularProgress />
      </Stack>
    );
  }

  if (agents.length === 0) {
    return (
      <Card>
        <CardContent>
          <Stack spacing={1.5}>
            <Typography variant="h6">No assistants yet</Typography>
            <Button variant="contained" onClick={onCreate}>
              Create assistant
            </Button>
          </Stack>
        </CardContent>
      </Card>
    );
  }

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2,1fr)', xl: 'repeat(3,1fr)' }, gap: 1.5 }}>
      {agents.map((agent) => (
        <Card key={agent.id}>
          <CardContent>
            <Stack spacing={1.2}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="subtitle1" noWrap>{agent.name}</Typography>
                <Chip size="small" label={agent.status} />
              </Stack>
              <Typography variant="body2" color="text.secondary">
                {agent.description || 'No description'}
              </Typography>
              <Stack direction="row" spacing={1}>
                <Button size="small" variant="outlined" onClick={() => onView(agent.id)}>Detail</Button>
                <Button size="small" variant="outlined" onClick={() => onEdit(agent.id)}>Edit</Button>
                <Button size="small" variant="contained" onClick={() => onChat(agent.id)}>Chat</Button>
              </Stack>
            </Stack>
          </CardContent>
        </Card>
      ))}
    </Box>
  );
}

function ListDetailAssistantsView({
  tenantId,
  loading,
  agents,
  onEdit,
  onChat,
  onCreate,
}: {
  tenantId: string;
  loading: boolean;
  agents: any[];
  onEdit: (id: string) => void;
  onChat: (id: string) => void;
  onCreate: () => void;
}) {
  const [agentQuery, setAgentQuery] = useState('');
  const [threadQuery, setThreadQuery] = useState('');
  const [selectedAgentId, setSelectedAgentId] = useState('');
  const [threads, setThreads] = useState<ThreadSummary[]>([]);
  const [selectedThreadId, setSelectedThreadId] = useState('');
  const [threadTurns, setThreadTurns] = useState<ThreadTurn[]>([]);
  const [threadsLoading, setThreadsLoading] = useState(false);
  const [threadDetailLoading, setThreadDetailLoading] = useState(false);
  const [error, setError] = useState('');

  const filteredAgents = useMemo(() => {
    const q = agentQuery.trim().toLowerCase();
    if (!q) return agents;
    return agents.filter((a) => `${a.name} ${a.description || ''}`.toLowerCase().includes(q));
  }, [agentQuery, agents]);

  const selectedAgent = useMemo(
    () => agents.find((a) => a.id === selectedAgentId) ?? filteredAgents[0] ?? null,
    [agents, filteredAgents, selectedAgentId]
  );

  const filteredThreads = useMemo(() => {
    const q = threadQuery.trim().toLowerCase();
    if (!q) return threads;
    return threads.filter((t) => `${t.threadKey} ${t.threadId}`.toLowerCase().includes(q));
  }, [threadQuery, threads]);

  const selectedThread = useMemo(
    () => threads.find((t) => t.threadId === selectedThreadId) ?? filteredThreads[0] ?? null,
    [threads, filteredThreads, selectedThreadId]
  );

  useEffect(() => {
    if (!selectedAgent && filteredAgents.length > 0) setSelectedAgentId(filteredAgents[0].id);
  }, [selectedAgent, filteredAgents]);

  useEffect(() => {
    const loadThreads = async () => {
      if (!selectedAgent) {
        setThreads([]);
        return;
      }
      try {
        setThreadsLoading(true);
        setError('');
        const res = await axios.get(`/api/v1/tenants/${tenantId}/threads?agentId=${selectedAgent.id}&limit=100`);
        const next = (res.data ?? []) as ThreadSummary[];
        setThreads(next);
        setSelectedThreadId((current) => (next.some((t) => t.threadId === current) ? current : next[0]?.threadId ?? ''));
      } catch (e: any) {
        setError(e?.message ?? 'No se pudo cargar conversaciones');
      } finally {
        setThreadsLoading(false);
      }
    };
    loadThreads();
  }, [tenantId, selectedAgent]);

  useEffect(() => {
    const loadThreadDetail = async () => {
      if (!selectedThread) {
        setThreadTurns([]);
        return;
      }
      try {
        setThreadDetailLoading(true);
        const res = await axios.get(`/api/v1/tenants/${tenantId}/threads/${selectedThread.threadId}/history?limit=50`);
        setThreadTurns((res.data?.turns ?? []) as ThreadTurn[]);
      } catch (e: any) {
        setError(e?.message ?? 'No se pudo cargar detalle');
      } finally {
        setThreadDetailLoading(false);
      }
    };
    loadThreadDetail();
  }, [tenantId, selectedThread]);

  return (
    <>
      {error ? <Alert severity="error" sx={{ mb: 1.5 }}>{error}</Alert> : null}
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', lg: '320px 360px minmax(0, 1fr)' },
          gap: 1.5,
          minHeight: 'calc(100vh - 200px)',
        }}
      >
        <Card>
          <CardHeader
            title={`Assistants ${agents.length}`}
            action={<Button size="small" variant="contained" onClick={onCreate}>Create</Button>}
          />
          <CardContent sx={{ pt: 0 }}>
            <TextField fullWidth size="small" placeholder="Search assistants" value={agentQuery} onChange={(e) => setAgentQuery(e.target.value)} />
            <Stack spacing={1} sx={{ mt: 1.5, maxHeight: '65vh', overflow: 'auto' }}>
              {loading ? <CircularProgress size={20} /> : filteredAgents.map((agent) => (
                <Box
                  key={agent.id}
                  onClick={() => setSelectedAgentId(agent.id)}
                  sx={{
                    border: '1px solid',
                    borderColor: selectedAgent?.id === agent.id ? 'primary.main' : 'divider',
                    borderRadius: 1.25,
                    p: 1.1,
                    cursor: 'pointer',
                    bgcolor: selectedAgent?.id === agent.id ? 'action.selected' : 'background.paper',
                  }}
                >
                  <Typography variant="subtitle2" noWrap>{agent.name}</Typography>
                  <Typography variant="caption" color="text.secondary">{agent.status}</Typography>
                </Box>
              ))}
            </Stack>
          </CardContent>
        </Card>

        <Card>
          <CardHeader
            title={`Conversations ${threads.length}`}
            action={
              <Button size="small" variant="outlined" disabled={!selectedAgent} onClick={() => selectedAgent && onChat(selectedAgent.id)}>
                New
              </Button>
            }
          />
          <CardContent sx={{ pt: 0 }}>
            <TextField fullWidth size="small" placeholder="Search conversations" value={threadQuery} onChange={(e) => setThreadQuery(e.target.value)} />
            <Stack spacing={1} sx={{ mt: 1.5, maxHeight: '65vh', overflow: 'auto' }}>
              {threadsLoading ? <CircularProgress size={20} /> : filteredThreads.map((thread) => (
                <Box
                  key={thread.threadId}
                  onClick={() => setSelectedThreadId(thread.threadId)}
                  sx={{
                    border: '1px solid',
                    borderColor: selectedThread?.threadId === thread.threadId ? 'primary.main' : 'divider',
                    borderRadius: 1.25,
                    p: 1.1,
                    cursor: 'pointer',
                    bgcolor: selectedThread?.threadId === thread.threadId ? 'action.selected' : 'background.paper',
                  }}
                >
                  <Typography variant="subtitle2" noWrap>{thread.threadKey || thread.threadId.slice(0, 14)}</Typography>
                  <Typography variant="caption" color="text.secondary">{thread.turnCount ?? 0} turns</Typography>
                </Box>
              ))}
            </Stack>
          </CardContent>
        </Card>

        <Card>
          <CardHeader
            title={selectedAgent?.name || 'Detail'}
            subheader={selectedThread ? `Thread ${selectedThread.threadKey || selectedThread.threadId.slice(0, 10)}` : 'No thread selected'}
            action={
              selectedAgent ? (
                <Stack direction="row" spacing={1}>
                  <Button size="small" variant="outlined" onClick={() => onEdit(selectedAgent.id)}>Tabs Editor</Button>
                  <Button size="small" variant="contained" onClick={() => onChat(selectedAgent.id)}>Open Chat</Button>
                </Stack>
              ) : null
            }
          />
          <Divider />
          <CardContent sx={{ maxHeight: '68vh', overflow: 'auto' }}>
            {threadDetailLoading ? (
              <CircularProgress size={22} />
            ) : threadTurns.length === 0 ? (
              <Typography variant="body2" color="text.secondary">No messages yet.</Typography>
            ) : (
              <Stack spacing={1.1}>
                {threadTurns.map((turn, idx) => (
                  <Box key={idx} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 1, p: 1 }}>
                    {turn.userMessage ? <Typography variant="body2"><strong>User:</strong> {turn.userMessage}</Typography> : null}
                    {turn.assistantResponse ? <Typography variant="body2" color="text.secondary"><strong>Assistant:</strong> {turn.assistantResponse}</Typography> : null}
                    {turn.timestamp ? <Typography variant="caption" color="text.disabled">{new Date(turn.timestamp).toLocaleString()}</Typography> : null}
                  </Box>
                ))}
              </Stack>
            )}
          </CardContent>
        </Card>
      </Box>
      <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
        <IconButton size="small"><Iconify icon="mdi:view-column-outline" width={16} /></IconButton>
        <Typography variant="caption" color="text.secondary">
          En lista-detalle, el botón `Tabs Editor` abre los tabs actuales de edición del asistente.
        </Typography>
      </Stack>
    </>
  );
}
