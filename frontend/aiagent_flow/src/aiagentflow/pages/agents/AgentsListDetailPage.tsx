import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardHeader from '@mui/material/CardHeader';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { useAgents } from './Hooks/useAgents';
import AgentDesignerPage from './Designer/AgentDesignerPage';

export default function AgentsListDetailPage() {
  const tenantId = useTenantId();
  const runtimeStorageKey = `af:agent:runtimeKind:${tenantId}`;
  const runtimeKind = typeof window !== 'undefined' ? localStorage.getItem(runtimeStorageKey) : null;
  const { agents, loading } = useAgents(tenantId, runtimeKind);
  const [query, setQuery] = useState('');
  const [selectedAgentId, setSelectedAgentId] = useState('');

  const filteredAgents = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return agents;
    return agents.filter((a) => `${a.name} ${a.description ?? ''}`.toLowerCase().includes(q));
  }, [agents, query]);

  const selectedAgent = useMemo(
    () => agents.find((a) => a.id === selectedAgentId) ?? filteredAgents[0] ?? null,
    [agents, filteredAgents, selectedAgentId]
  );

  useEffect(() => {
    if (!selectedAgent && filteredAgents.length > 0) setSelectedAgentId(filteredAgents[0].id);
  }, [filteredAgents, selectedAgent]);

  return (
    <>
      <Helmet>
        <title>Asistentes | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth={false}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1.5 }}>
          <Typography variant="h4">Asistentes</Typography>
          <Stack direction="row" spacing={1}>
            <Button
              component={RouterLink}
              href={paths.dashboard.agents}
              variant="outlined"
              startIcon={<Iconify icon="mdi:view-grid-outline" />}
            >
              Vista cards
            </Button>
            <Button component={RouterLink} href={paths.dashboard.agentDesigner} variant="contained" startIcon={<Iconify icon="mingcute:add-line" />}>
              Nuevo asistente
            </Button>
          </Stack>
        </Stack>

        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', lg: '310px minmax(0, 1fr)' },
            gap: 1.5,
            minHeight: 'calc(100vh - 220px)',
          }}
        >
          <Card>
            <CardHeader title={`Asistentes ${agents.length}`} />
            <CardContent sx={{ pt: 0 }}>
              <TextField fullWidth size="small" placeholder="Buscar asistentes" value={query} onChange={(e) => setQuery(e.target.value)} />
              <Stack spacing={1} sx={{ mt: 1.5, maxHeight: '70vh', overflow: 'auto' }}>
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
                    <Typography variant="caption" color="text.secondary">{agent.runtimeKind || 'Texto'} · {agent.status}</Typography>
                  </Box>
                ))}
              </Stack>
            </CardContent>
          </Card>

          <Card>
            <CardHeader
              title={selectedAgent?.name || 'Detalle del asistente'}
              subheader={selectedAgent?.id ? `${selectedAgent.id.slice(0, 8)}...` : 'Selecciona un asistente'}
              action={
                selectedAgent ? (
                  <Stack direction="row" spacing={1}>
                    <Button size="small" variant="contained" component={RouterLink} href={`${paths.dashboard.agents}/${selectedAgent.id}/chat`}>
                      Hablar
                    </Button>
                  </Stack>
                ) : null
              }
            />
            <CardContent sx={{ p: 0, height: '72vh' }}>
              {selectedAgent ? (
                <Box sx={{ height: '100%', overflow: 'auto' }}>
                  <AgentDesignerPage embedded embeddedAgentId={selectedAgent.id} />
                </Box>
              ) : (
                <Stack alignItems="center" justifyContent="center" sx={{ height: '100%' }}>
                  <Typography variant="body2" color="text.secondary">Selecciona un asistente para abrir su Diseñador.</Typography>
                </Stack>
              )}
            </CardContent>
          </Card>
        </Box>
      </DashboardContent>
    </>
  );
}
