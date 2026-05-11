import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Tabs from '@mui/material/Tabs';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { useRouter, useParams } from 'src/routes/hooks';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';

const statusColor = (status?: string) => {
  if (status === 'Published') return 'success';
  if (status === 'Draft') return 'warning';
  if (status === 'Archived') return 'error';
  return 'default';
};

const countItems = (value: unknown) => {
  if (Array.isArray(value)) return value.length;
  if (value && typeof value === 'object') return Object.keys(value).length;
  return 0;
};

const readAgentModel = (agent: any) =>
  agent?.modelId || agent?.model || agent?.brain?.model || agent?.runtime?.model || 'Modelo por defecto';

const readAgentInstructions = (agent: any) =>
  agent?.instructions || agent?.systemPrompt || agent?.brain?.instructions || 'Sin instrucciones publicadas.';

const readAgentTools = (agent: any) =>
  agent?.availableTools || agent?.tools || agent?.brain?.tools || agent?.toolKeys || [];

export default function AgentDetailPage() {
  const theme = useTheme();
  const router = useRouter();
  const { id } = useParams();
  const tenantId = useTenantId();

  const [agent, setAgent] = useState<any>(null);
  const [executions, setExecutions] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [currentTab, setCurrentTab] = useState('studio');
  const [allowedTargets, setAllowedTargets] = useState<string[]>([]);
  const [allowlistVisible, setAllowlistVisible] = useState(false);
  const [targetAgentInput, setTargetAgentInput] = useState('');
  const [candidateTargets, setCandidateTargets] = useState<string[]>([]);
  const [decisionLoading, setDecisionLoading] = useState(false);
  const [policyDecision, setPolicyDecision] = useState<{
    allowed: boolean;
    reason: string;
    hasExplicitPolicy: boolean;
  } | null>(null);

  const fetchAgentDetail = useCallback(async () => {
    if (!id) return;

    try {
      setLoading(true);
      const [agentRes, executionsRes] = await Promise.all([
        axios.get(endpoints.agentflow.agents.detail(tenantId, id as string)),
        axios.get(endpoints.agentflow.executions.byAgent(tenantId, id as string), {
          params: { limit: 10 },
        }),
      ]);

      setAgent(agentRes.data);
      setExecutions(executionsRes.data ?? []);

      try {
        const agentsRes = await axios.get(endpoints.agentflow.agents.list(tenantId));
        const targets = (agentsRes.data ?? [])
          .filter((item: any) => item?.id && item.id !== id)
          .filter((item: any) => item?.status !== 'Archived')
          .map((item: any) => item.id as string);
        setCandidateTargets(targets);
      } catch {
        setCandidateTargets([]);
      }

      try {
        const allowRes = await axios.get(
          endpoints.agentflow.executions.handoffAllowedTargets(tenantId, id as string)
        );
        setAllowedTargets(allowRes.data?.allowedTargets ?? allowRes.data?.targets ?? []);
        setAllowlistVisible(true);
      } catch {
        setAllowedTargets([]);
        setAllowlistVisible(false);
      }
    } finally {
      setLoading(false);
    }
  }, [id, tenantId]);

  useEffect(() => {
    void fetchAgentDetail();
  }, [fetchAgentDetail]);

  const evaluateDecision = async (target?: string) => {
    const targetValue = (target ?? targetAgentInput).trim();

    if (!id || !targetValue) {
      setPolicyDecision(null);
      return;
    }

    try {
      setDecisionLoading(true);
      const response = await axios.get(
        endpoints.agentflow.executions.handoffDecision(tenantId, id as string, targetValue)
      );
      setPolicyDecision({
        allowed: !!response.data?.allowed,
        reason: response.data?.reason ?? 'unknown',
        hasExplicitPolicy: !!response.data?.hasExplicitPolicy,
      });
    } catch {
      setPolicyDecision({ allowed: false, reason: 'evaluation_failed', hasExplicitPolicy: false });
    } finally {
      setDecisionLoading(false);
    }
  };

  if (loading) {
    return (
      <DashboardContent maxWidth="xl">
        <LinearProgress />
      </DashboardContent>
    );
  }

  if (!agent) {
    return (
      <DashboardContent maxWidth="xl">
        <Box sx={{ py: 10, textAlign: 'center' }}>
          <Typography variant="h6" color="text.secondary">
            Agente no encontrado
          </Typography>
          <Button variant="outlined" onClick={() => router.back()} sx={{ mt: 2 }}>
            Volver
          </Button>
        </Box>
      </DashboardContent>
    );
  }

  const tools = readAgentTools(agent);
  const model = readAgentModel(agent);
  const instructions = readAgentInstructions(agent);
  const memoryCount = countItems(agent.memory || agent.knowledge || agent.dataSources);
  const channelCount = countItems(agent.channels || agent.channelIds || agent.channelAssignments);

  return (
    <>
      <Helmet>
        <title>{agent.name} | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack spacing={3}>
          <Button
            color="inherit"
            startIcon={<Iconify icon="eva:arrow-back-fill" />}
            onClick={() => router.back()}
            sx={{ alignSelf: 'flex-start' }}
          >
            Volver a agentes
          </Button>

          <Card
            sx={{
              overflow: 'hidden',
              border: `1px solid ${alpha(theme.palette.primary.main, 0.18)}`,
              background: `linear-gradient(135deg, ${alpha(theme.palette.primary.main, 0.1)}, ${alpha(
                theme.palette.info.main,
                0.08
              )})`,
            }}
          >
            <CardContent>
              <Grid container spacing={3} alignItems="center">
                <Grid item xs={12} md={8}>
                  <Stack spacing={1.5}>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                      <Typography variant="h3">{agent.name}</Typography>
                      <Label color={statusColor(agent.status)}>{agent.status || 'Sin estado'}</Label>
                      {agent.version && <Chip size="small" label={`v${agent.version}`} />}
                    </Stack>
                    <Typography variant="body1" color="text.secondary">
                      {agent.description ||
                        'Este agente es un subflujo inteligente: entiende una tarea, usa herramientas autorizadas y devuelve una salida al workflow principal.'}
                    </Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Chip icon={<Iconify icon="mdi:robot-outline" />} label={model} />
                      <Chip icon={<Iconify icon="mdi:tools" />} label={`${countItems(tools)} tools`} />
                      <Chip icon={<Iconify icon="mdi:database-outline" />} label={`${memoryCount} fuentes`} />
                      <Chip icon={<Iconify icon="mdi:access-point" />} label={`${channelCount} canales`} />
                    </Stack>
                  </Stack>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Stack spacing={1} direction={{ xs: 'column', sm: 'row', md: 'column' }}>
                    <Button
                      fullWidth
                      variant="contained"
                      startIcon={<Iconify icon="solar:pen-bold" />}
                      href={paths.dashboard.agentEdit(agent.id)}
                    >
                      Editar subflujo
                    </Button>
                    <Button
                      fullWidth
                      variant="outlined"
                      startIcon={<Iconify icon="mdi:source-branch" />}
                      href={paths.dashboard.workflows}
                    >
                      Usar en Workflow Studio
                    </Button>
                  </Stack>
                </Grid>
              </Grid>
            </CardContent>
          </Card>

          <Tabs value={currentTab} onChange={(_, value) => setCurrentTab(value)}>
            <Tab value="studio" label="Subflujo" />
            <Tab value="handoff" label="Rutas y permisos" />
            <Tab value="executions" label="Ejecuciones" />
          </Tabs>

          {currentTab === 'studio' && (
            <Grid container spacing={3}>
              {[
                {
                  title: 'Entrada e instrucciones',
                  icon: 'mdi:message-processing-outline',
                  body: instructions,
                },
                {
                  title: 'Herramientas autorizadas',
                  icon: 'mdi:tools',
                  body:
                    countItems(tools) > 0
                      ? (Array.isArray(tools) ? tools : Object.keys(tools)).join(', ')
                      : 'Sin tools autorizadas. Agregalas desde Agent Studio antes de usar el nodo en un flujo.',
                },
                {
                  title: 'Conocimiento y memoria',
                  icon: 'mdi:database-search-outline',
                  body: `${memoryCount} fuente(s) disponibles para responder o tomar decisiones.`,
                },
                {
                  title: 'Salida al workflow',
                  icon: 'mdi:call-split',
                  body:
                    'El nodo AI Agent devuelve resultado, variables y estados: finalizo, expiro o hubo error. El workflow decide el siguiente paso.',
                },
              ].map((item) => (
                <Grid key={item.title} item xs={12} md={6}>
                  <Card variant="outlined" sx={{ height: '100%' }}>
                    <CardContent>
                      <Stack spacing={1.5}>
                        <Iconify icon={item.icon} width={28} sx={{ color: 'primary.main' }} />
                        <Typography variant="h6">{item.title}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {item.body}
                        </Typography>
                      </Stack>
                    </CardContent>
                  </Card>
                </Grid>
              ))}

              <Grid item xs={12}>
                <Card variant="outlined">
                  <CardContent>
                    <Stack spacing={2}>
                      <Typography variant="h6">Como se usa este agente dentro de Workflow Studio</Typography>
                      <Grid container spacing={2}>
                        {[
                          ['1', 'Arrastra un nodo Agente de IA al canvas.'],
                          ['2', `Selecciona ${agent.name} como agente publicado.`],
                          ['3', 'Define que variable recibe y que variable devuelve.'],
                          ['4', 'Conecta finalizo, expiro o error con acciones de negocio.'],
                        ].map(([step, text]) => (
                          <Grid key={step} item xs={12} md={3}>
                            <Stack spacing={1}>
                              <Chip label={step} sx={{ width: 32 }} />
                              <Typography variant="body2">{text}</Typography>
                            </Stack>
                          </Grid>
                        ))}
                      </Grid>
                    </Stack>
                  </CardContent>
                </Card>
              </Grid>
            </Grid>
          )}

          {currentTab === 'handoff' && (
            <Grid container spacing={3}>
              <Grid item xs={12} md={5}>
                <Card variant="outlined">
                  <CardContent>
                    <Stack spacing={2}>
                      <Typography variant="h6">Permisos de traspaso</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Controla que otros agentes puede llamar este agente cuando una intencion requiere
                        una tarea especializada.
                      </Typography>
                      {!allowlistVisible && (
                        <Alert severity="info">
                          La politica no esta visible para tu rol actual o todavia no fue configurada.
                        </Alert>
                      )}
                      {allowlistVisible && allowedTargets.length === 0 && (
                        <Alert severity="warning">No hay agentes destino permitidos.</Alert>
                      )}
                      <Stack direction="row" spacing={1} flexWrap="wrap">
                        {allowedTargets.map((target) => (
                          <Chip key={target} label={target} />
                        ))}
                      </Stack>
                    </Stack>
                  </CardContent>
                </Card>
              </Grid>

              <Grid item xs={12} md={7}>
                <Card variant="outlined">
                  <CardContent>
                    <Stack spacing={2}>
                      <Typography variant="h6">Probar decision</Typography>
                      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
                        <TextField
                          select
                          fullWidth
                          label="Agente destino"
                          value={targetAgentInput}
                          onChange={(event) => setTargetAgentInput(event.target.value)}
                        >
                          {candidateTargets.map((target) => (
                            <MenuItem key={target} value={target}>
                              {target}
                            </MenuItem>
                          ))}
                        </TextField>
                        <Button
                          variant="contained"
                          disabled={!targetAgentInput || decisionLoading}
                          onClick={() => evaluateDecision()}
                        >
                          Validar
                        </Button>
                      </Stack>
                      {policyDecision && (
                        <Alert severity={policyDecision.allowed ? 'success' : 'error'}>
                          {policyDecision.allowed ? 'Permitido' : 'Bloqueado'} por politica: {policyDecision.reason}.{' '}
                          {policyDecision.hasExplicitPolicy ? 'Regla explicita encontrada.' : 'Se uso politica por defecto.'}
                        </Alert>
                      )}
                    </Stack>
                  </CardContent>
                </Card>
              </Grid>
            </Grid>
          )}

          {currentTab === 'executions' && (
            <Card>
              <CardContent>
                <Stack spacing={2}>
                  <Typography variant="h6">Ultimas ejecuciones</Typography>
                  {executions.length === 0 && (
                    <Alert severity="info">Todavia no hay ejecuciones registradas para este agente.</Alert>
                  )}
                  {executions.map((execution) => (
                    <Box key={execution.id}>
                      <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="space-between">
                        <Box>
                          <Typography variant="subtitle2">{execution.id}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {execution.createdAt || execution.startedAt || 'sin fecha'}
                          </Typography>
                        </Box>
                        <Stack direction="row" spacing={1} alignItems="center">
                          <Chip size="small" label={execution.status || 'unknown'} />
                          <Button size="small" href={paths.dashboard.executionDetail(execution.id)}>
                            Ver detalle
                          </Button>
                        </Stack>
                      </Stack>
                      <Divider sx={{ my: 1.5 }} />
                    </Box>
                  ))}
                </Stack>
              </CardContent>
            </Card>
          )}
        </Stack>
      </DashboardContent>
    </>
  );
}
