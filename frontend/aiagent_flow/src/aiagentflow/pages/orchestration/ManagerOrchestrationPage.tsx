import type { GridColDef } from '@mui/x-data-grid';

import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import { DataGrid } from '@mui/x-data-grid';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { TermHelp } from 'src/aiagentflow/components/TermHelp';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

const DEFAULT_EXAMPLES = ['quiero agendar una cita', 'necesito pagar', 'quiero validar mi identidad'];

const CHANNEL_LABELS: Record<string, string> = {
  whatsapp: 'WhatsApp',
  web: 'Web chat',
  voice: 'Voz',
  callcenter: 'Call center',
  email: 'Email',
};

type Agent = { id: string; name: string; status?: string };

type IntentRule = {
  id: string;
  tenantId: string;
  intentKey: string;
  intentDescription?: string;
  sourceAgentId: string;
  targetAgentId: string;
  priority: number;
  enabled: boolean;
  channel?: string;
  conditionsJson?: string;
  handoffPolicyJson?: string;
  version: number;
  updatedAt: string;
};

type IntentRuleView = IntentRule & {
  workflowId?: string;
  workflowName?: string;
  eventName?: string;
  examples?: string[];
  confidenceThreshold?: number;
};

type RoutingAgent = {
  id: string;
  agentId: string;
  agentType: string;
  enabled: boolean;
  testModeAllowed: boolean;
  externalReplyAllowed: boolean;
  capabilities: string[];
  updatedAt: string;
};

const parseContext = (json?: string) => {
  try {
    return JSON.parse(json || '{}') as {
      workflowId?: string;
      workflowName?: string;
      eventName?: string;
      examples?: string[];
      confidenceThreshold?: number;
    };
  } catch {
    return {};
  }
};

const agentName = (agents: Agent[], id?: string) => agents.find((agent) => agent.id === id)?.name || id || 'Sin agente';

export default function ManagerOrchestrationPage() {
  const tenantId = useTenantId();

  const [agents, setAgents] = useState<Agent[]>([]);
  const [rules, setRules] = useState<IntentRule[]>([]);
  const [intentAgents, setIntentAgents] = useState<RoutingAgent[]>([]);
  const [sourceAgentId, setSourceAgentId] = useState('');
  const [targetAgentId, setTargetAgentId] = useState('');
  const [ruleIntentKey, setRuleIntentKey] = useState('agendar_cita');
  const [ruleDescription, setRuleDescription] = useState('');
  const [ruleChannel, setRuleChannel] = useState('whatsapp');
  const [ruleExamples, setRuleExamples] = useState(DEFAULT_EXAMPLES.join('\n'));
  const [probeText, setProbeText] = useState(DEFAULT_EXAMPLES[0]);
  const [probeChannel, setProbeChannel] = useState('whatsapp');
  const [simulateResult, setSimulateResult] = useState<any>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      const [agentsRes, rulesRes, intentAgentsRes] = await Promise.all([
        axios.get(endpoints.agentflow.agents.list(tenantId)),
        axios.get(endpoints.agentflow.intentRouting.rules(tenantId)),
        axios.get(endpoints.agentflow.intentRouting.agents(tenantId)),
      ]);

      const agentList = (agentsRes.data ?? [])
        .filter((item: any) => item?.id)
        .map((item: any) => ({ id: item.id, name: item.name || item.id, status: item.status }));

      setAgents(agentList);
      setRules(rulesRes.data ?? []);
      setIntentAgents(intentAgentsRes.data ?? []);

      if (!sourceAgentId && agentList[0]) setSourceAgentId(agentList[0].id);
      if (!targetAgentId && agentList[1]) setTargetAgentId(agentList[1].id);
    } catch (error: any) {
      setMessage(error?.message || 'No se pudo cargar el mapa de intenciones.');
    } finally {
      setLoading(false);
    }
  }, [sourceAgentId, targetAgentId, tenantId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const intentRows = useMemo<IntentRuleView[]>(
    () =>
      rules.map((rule) => ({
        ...rule,
        ...parseContext(rule.conditionsJson),
      })),
    [rules]
  );

  const enabledRules = intentRows.filter((rule) => rule.enabled).length;
  const workflowCount = new Set(intentRows.map((rule) => rule.workflowId).filter(Boolean)).size;
  const channelCount = new Set(intentRows.map((rule) => rule.channel).filter(Boolean)).size;

  const createRule = async () => {
    if (!sourceAgentId || !targetAgentId || !ruleIntentKey.trim()) {
      setMessage('Selecciona agente origen, agente destino e intencion.');
      return;
    }

    const examples = ruleExamples
      .split('\n')
      .map((example) => example.trim())
      .filter(Boolean);

    try {
      await axios.post(endpoints.agentflow.intentRouting.rules(tenantId), {
        intentKey: ruleIntentKey.trim(),
        intentDescription: ruleDescription.trim() || undefined,
        examplePhrases: examples,
        sourceAgentId,
        targetAgentId,
        priority: 100,
        enabled: true,
        channel: ruleChannel || null,
        conditionsJson: JSON.stringify({
          eventName: 'connect.message.received',
          examples,
          confidenceThreshold: 0.72,
        }),
        handoffPolicyJson: JSON.stringify({ mode: 'allow', reason: 'created_from_intent_map' }),
      });
      setMessage('Intencion sincronizada. El orquestador ya puede evaluarla en el canal seleccionado.');
      await loadData();
    } catch (error: any) {
      setMessage(error?.message || 'No se pudo crear la regla de intencion.');
    }
  };

  const simulate = async () => {
    if (!sourceAgentId || !probeText.trim()) return;

    try {
      const response = await axios.post(endpoints.agentflow.intentRouting.simulate(tenantId), {
        sourceAgentId,
        channel: probeChannel,
        text: probeText,
        input: probeText,
      });
      setSimulateResult(response.data);
      setMessage('Prueba ejecutada. Revisa el resultado antes de publicar cambios en canales reales.');
    } catch (error: any) {
      setSimulateResult(null);
      setMessage(error?.message || 'No se pudo simular el enrutamiento.');
    }
  };

  const columns: GridColDef<IntentRuleView>[] = [
    {
      field: 'intentKey',
      headerName: 'Motivo',
      flex: 1,
      minWidth: 160,
      renderCell: (params) => (
        <Stack spacing={0.2}>
          <Typography variant="subtitle2">{params.row.intentKey}</Typography>
          {params.row.intentDescription && (
            <Typography variant="caption" color="text.secondary" noWrap>{params.row.intentDescription}</Typography>
          )}
        </Stack>
      ),
    },
    {
      field: 'channel',
      headerName: 'Canal',
      width: 130,
      renderCell: (params) => CHANNEL_LABELS[params.row.channel || ''] || params.row.channel || 'Todos',
    },
    {
      field: 'workflowName',
      headerName: 'Flujo',
      flex: 1,
      minWidth: 180,
      renderCell: (params) => params.row.workflowName || params.row.workflowId || 'Pendiente de asociar',
    },
    {
      field: 'sourceAgentId',
      headerName: 'Asistente que recibe',
      flex: 1,
      minWidth: 180,
      renderCell: (params) => agentName(agents, params.row.sourceAgentId),
    },
    {
      field: 'targetAgentId',
      headerName: 'Asistente que resuelve',
      flex: 1,
      minWidth: 180,
      renderCell: (params) => agentName(agents, params.row.targetAgentId),
    },
    {
      field: 'examples',
      headerName: 'Ejemplos',
      flex: 1,
      minWidth: 220,
      sortable: false,
      renderCell: (params) => (params.row.examples || []).slice(0, 2).join(', ') || 'Sin ejemplos',
    },
    {
      field: 'enabled',
      headerName: 'Estado',
      width: 120,
      renderCell: (params) => (
        <Chip
          size="small"
          color={params.row.enabled ? 'success' : 'default'}
          label={params.row.enabled ? 'Activo' : 'Pausado'}
        />
      ),
    },
  ];

  return (
    <>
      <Helmet>
        <title>Motivos del cliente | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack spacing={3}>
          <Card
            sx={{
              color: 'common.white',
              background: 'linear-gradient(135deg, #071923 0%, #0A6B76 55%, #12B8A6 100%)',
            }}
          >
            <CardContent>
              <Grid container spacing={3} alignItems="center">
                <Grid item xs={12} md={8}>
                    <Stack spacing={1.5}>
                    <Stack direction="row" spacing={0.75} alignItems="center">
                      <Typography variant="h3">Motivos del cliente</Typography>
                      <TermHelp title="Intención es el termino técnico para el motivo o necesidad que expresa el cliente, por ejemplo agendar, pagar o validar identidad." />
                    </Stack>
                    <Typography sx={{ opacity: 0.86 }}>
                      Aquí defines qué necesita el cliente, qué frases lo representan y qué asistente o flujo debe hacerse cargo.
                    </Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Chip label={`${enabledRules} motivos activos`} />
                      <Chip label={`${workflowCount} flujos conectados`} />
                      <Chip label={`${channelCount || 'Todos'} canales`} />
                    </Stack>
                  </Stack>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Stack spacing={1}>
                    <Button variant="contained" color="inherit" href={paths.dashboard.workflows}>
                      Abrir Workflow Studio
                    </Button>
                    <Button variant="outlined" color="inherit" href={paths.dashboard.agents}>
                      Ver agentes
                    </Button>
                  </Stack>
                </Grid>
              </Grid>
            </CardContent>
          </Card>

          {message && <Alert severity="info" onClose={() => setMessage(null)}>{message}</Alert>}

          <Grid container spacing={3}>
            {[
              ['1', 'El canal recibe un mensaje', 'WhatsApp, voz, web chat o email generan un evento del sistema.'],
                ['2', 'El sistema entiende la necesidad', 'Compara la frase con ejemplos y reglas activas para detectar el motivo correcto.'],
                ['3', 'Se activa el asistente correcto', 'El asistente publicado atiende la tarea o la deriva a otro flujo.'],
                ['4', 'El flujo continua', 'Según el resultado, conecta pagos, KYC, revisión humana, almacenamiento o herramientas externas.'],
            ].map(([step, title, body]) => (
              <Grid key={step} item xs={12} md={3}>
                <Card variant="outlined" sx={{ height: '100%' }}>
                  <CardContent>
                    <Stack spacing={1}>
                      <Chip label={step} sx={{ width: 32 }} />
                      <Typography variant="subtitle1">{title}</Typography>
                      <Typography variant="body2" color="text.secondary">{body}</Typography>
                    </Stack>
                  </CardContent>
                </Card>
              </Grid>
            ))}
          </Grid>

          <Grid container spacing={3}>
            <Grid item xs={12} md={5}>
              <Card>
                <CardContent>
                  <Stack spacing={2}>
                    <Typography variant="h6">Probar un mensaje</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Usa esto para confirmar si el sistema entiende el motivo correcto antes de activar un flujo real.
                    </Typography>
                    <TextField
                      select
                      label="Asistente que recibe primero"
                      value={sourceAgentId}
                      onChange={(event) => setSourceAgentId(event.target.value)}
                      fullWidth
                    >
                      {agents.map((agent) => (
                        <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      select
                      label="Canal"
                      value={probeChannel}
                      onChange={(event) => setProbeChannel(event.target.value)}
                      fullWidth
                    >
                      {Object.entries(CHANNEL_LABELS).map(([value, label]) => (
                        <MenuItem key={value} value={value}>{label}</MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      label="Mensaje de ejemplo"
                      value={probeText}
                      onChange={(event) => setProbeText(event.target.value)}
                      multiline
                      minRows={3}
                      fullWidth
                    />
                    <Button variant="contained" onClick={simulate} disabled={loading || !sourceAgentId}>
                      Simular decision
                    </Button>
                    {simulateResult && (
                      <Alert severity="success">
                        Resultado: {simulateResult.intentKey || simulateResult.intent || 'sin motivo detectado'} - destino:{' '}
                        {agentName(agents, simulateResult.targetAgentId)}
                      </Alert>
                    )}
                  </Stack>
                </CardContent>
              </Card>
            </Grid>

            <Grid item xs={12} md={7}>
              <Card>
                <CardContent>
                  <Stack spacing={2}>
                    <Typography variant="h6">Nuevo motivo del cliente</Typography>
                    <Typography variant="body2" color="text.secondary">
                      El evento técnico lo resuelve el sistema. Aquí solo defines el motivo, el canal y quién debe hacerse cargo.
                    </Typography>
                    <Grid container spacing={2}>
                      <Grid item xs={12} md={6}>
                        <TextField
                          label="Nombre corto del motivo"
                          value={ruleIntentKey}
                          onChange={(event) => setRuleIntentKey(event.target.value)}
                          fullWidth
                          helperText="Ejemplo: agendar_cita, pagar_credito, validar_identidad"
                        />
                      </Grid>
                      <Grid item xs={12} md={6}>
                        <TextField
                          select
                          label="Canal"
                          value={ruleChannel}
                          onChange={(event) => setRuleChannel(event.target.value)}
                          fullWidth
                        >
                          {Object.entries(CHANNEL_LABELS).map(([value, label]) => (
                            <MenuItem key={value} value={value}>{label}</MenuItem>
                          ))}
                        </TextField>
                      </Grid>
                      <Grid item xs={12} md={6}>
                        <TextField
                          select
                          label="Asistente que recibe"
                          value={sourceAgentId}
                          onChange={(event) => setSourceAgentId(event.target.value)}
                          fullWidth
                        >
                          {agents.map((agent) => (
                            <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
                          ))}
                        </TextField>
                      </Grid>
                      <Grid item xs={12} md={6}>
                        <TextField
                          select
                          label="Asistente que resuelve"
                          value={targetAgentId}
                          onChange={(event) => setTargetAgentId(event.target.value)}
                          fullWidth
                        >
                          {agents
                            .filter((agent) => agent.id !== sourceAgentId)
                            .map((agent) => (
                              <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
                            ))}
                        </TextField>
                      </Grid>
                      <Grid item xs={12}>
                        <TextField
                          label="Descripcion del motivo"
                          value={ruleDescription}
                          onChange={(event) => setRuleDescription(event.target.value)}
                          fullWidth
                          helperText="Describe en lenguaje natural cuándo debe activarse este motivo. El router usa esta guía para entender mensajes."
                          placeholder="El cliente quiere agendar una cita medica o de servicio"
                        />
                      </Grid>
                      <Grid item xs={12}>
                        <TextField
                          label="Frases de ejemplo"
                          value={ruleExamples}
                          onChange={(event) => setRuleExamples(event.target.value)}
                          multiline
                          minRows={3}
                          fullWidth
                          helperText="Una frase por línea. Estas frases ayudan al sistema a clasificar mejor cada mensaje."
                        />
                      </Grid>
                    </Grid>
                    <Button variant="contained" onClick={createRule} startIcon={<Iconify icon="mdi:plus" />}>
                      Crear y publicar
                    </Button>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          </Grid>

          <Card>
            <CardContent>
              <Stack spacing={2}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Box>
                    <Typography variant="h6">Motivos publicados</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Lista operativa que usa el sistema para decidir quién atiende cada mensaje o llamada.
                    </Typography>
                  </Box>
                  <Button variant="outlined" onClick={() => loadData()} disabled={loading}>
                    Actualizar
                  </Button>
                </Stack>
                <DataGrid
                  rows={intentRows}
                  columns={columns}
                  autoHeight
                  loading={loading}
                  disableRowSelectionOnClick
                  pageSizeOptions={[10, 25, 50]}
                  initialState={{ pagination: { paginationModel: { pageSize: 10 } } }}
                />
              </Stack>
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardContent>
              <Stack spacing={2}>
                <Typography variant="h6">Agente IA del sistema</Typography>
                <Typography variant="body2" color="text.secondary">
                  Este asistente no se edita desde el flujo normal. Su responsabilidad es asistir al usuario,
                  explicar capacidades, validar configuraciones y ayudar a convertir necesidades de negocio en motivos entendibles por el sistema.
                </Typography>
                <Divider />
                <Grid container spacing={2}>
                  <Grid item xs={12} md={4}>
                    <Typography variant="subtitle2">Agentes registrados</Typography>
                    <Typography variant="h4">{intentAgents.length}</Typography>
                  </Grid>
                  <Grid item xs={12} md={4}>
                    <Typography variant="subtitle2">Modo prueba</Typography>
                    <Typography variant="h4">
                      {intentAgents.filter((agent) => agent.testModeAllowed).length}
                    </Typography>
                  </Grid>
                  <Grid item xs={12} md={4}>
                    <Typography variant="subtitle2">Respuesta externa</Typography>
                    <Typography variant="h4">
                      {intentAgents.filter((agent) => agent.externalReplyAllowed).length}
                    </Typography>
                  </Grid>
                </Grid>
              </Stack>
            </CardContent>
          </Card>
        </Stack>
      </DashboardContent>
    </>
  );
}
