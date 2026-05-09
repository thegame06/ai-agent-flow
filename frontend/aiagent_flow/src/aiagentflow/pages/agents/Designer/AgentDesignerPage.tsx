import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useParams } from 'react-router';
import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Tabs from '@mui/material/Tabs';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Paper from '@mui/material/Paper';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import Slider from '@mui/material/Slider';
import Select from '@mui/material/Select';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import InputLabel from '@mui/material/InputLabel';
import IconButton from '@mui/material/IconButton';
import CardContent from '@mui/material/CardContent';
import FormControl from '@mui/material/FormControl';
import { alpha, useTheme } from '@mui/material/styles';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';

import AgentFlowCanvas from './AgentFlowCanvas';
import { saveAgent, publishAgent, fetchAgentDetail } from './designerThunks';
import {
  addStep,
  addTool,
  removeStep,
  removeTool,
  resetDraft,
  updateStep,
  updateField,
  updateModel,
  setActiveTab,
  updateMemory,
  updateGuardrails,
} from './designerSlice';

import type { AgentStep } from './types';


// â”€â”€ Helpers â”€â”€
const STEP_TYPES = [
  { value: 'think', label: 'Razonar', icon: 'mdi:head-lightbulb', color: '#7C4DFF' },
  { value: 'plan', label: 'Planificar', icon: 'mdi:map-outline', color: '#00BCD4' },
  { value: 'act', label: 'Actuar', icon: 'mdi:lightning-bolt', color: '#FF9800' },
  { value: 'observe', label: 'Observar', icon: 'mdi:eye-outline', color: '#4CAF50' },
  { value: 'decide', label: 'Decision', icon: 'mdi:source-branch', color: '#E91E63' },
  { value: 'aggregate', label: 'Agregador', icon: 'mdi:graph-outline', color: '#3F51B5' },
  { value: 'tool_call', label: 'Tool', icon: 'mdi:wrench-outline', color: '#607D8B' },
  { value: 'human_review', label: 'Revision humana', icon: 'mdi:account-check', color: '#795548' },
] as const;

const FALLBACK_MODELS = ['gpt-4o', 'gpt-4o-mini', 'claude-3.5-sonnet', 'gemini-2.0-flash'];

let stepCounter = 0;
const genId = () => `step-${++stepCounter}-${Date.now()}`;

function defaultConfigForStepType(type: AgentStep['type']): Record<string, unknown> {
  switch (type) {
    case 'think':
    case 'plan':
      return { prompt: '', outputKey: 'latest' };
    case 'act':
    case 'tool_call':
      return { toolName: '', inputTemplate: '{{input}}' };
    case 'decide':
      return { mode: 'contains', matchValue: 'approved' };
    case 'aggregate':
      return { strategy: 'concat', separator: '\n---\n' };
    case 'human_review':
      return { reason: 'Manual verification required' };
    default:
      return {};
  }
}

interface ModelOption {
  modelId: string;
  providerId: string;
  displayName: string;
}

interface ToolOption {
  extensionId: string;
  name: string;
  version: string;
  riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
  description: string;
}

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// TAB PANELS
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

function TabGeneral({ draft, dispatch }: { draft: any; dispatch: any }) {
  return (
    <Stack spacing={3}>
      <TextField
        fullWidth
        label="Nombre del agente"
        value={draft.name}
        onChange={(e) => dispatch(updateField({ field: 'name', value: e.target.value }))}
        placeholder="e.g. CustomerSupport-v2"
      />
      <TextField
        fullWidth
        multiline
        rows={3}
        label="Descripcion"
        value={draft.description}
        onChange={(e) => dispatch(updateField({ field: 'description', value: e.target.value }))}
        placeholder="Describe the agent's purpose and capabilities..."
      />
      <Stack direction="row" spacing={2}>
        <TextField
          label="Version"
          value={draft.version}
          onChange={(e) => dispatch(updateField({ field: 'version', value: e.target.value }))}
          sx={{ width: 150 }}
        />
        <FormControl sx={{ minWidth: 150 }}>
          <InputLabel>Estado</InputLabel>
          <Select
            value={draft.status}
            label="Estado"
            onChange={(e) => dispatch(updateField({ field: 'status', value: e.target.value }))}
          >
            <MenuItem value="Draft">Draft</MenuItem>
            <MenuItem value="Published">Published</MenuItem>
            <MenuItem value="Archived">Archived</MenuItem>
          </Select>
        </FormControl>
      </Stack>
      <TextField
        fullWidth
        multiline
        rows={6}
        label="Instrucciones del agente"
        value={draft.systemPrompt}
        onChange={(e) => dispatch(updateField({ field: 'systemPrompt', value: e.target.value }))}
        placeholder="You are a helpful agent that..."
        sx={{ fontFamily: 'monospace' }}
      />
      <Box>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>Etiquetas</Typography>
        <Stack direction="row" spacing={1} flexWrap="wrap">
          {draft.tags.map((tag: string, i: number) => (
            <Chip
              key={i}
              label={tag}
              onDelete={() => {
                const newEtiquetas = draft.tags.filter((_: string, idx: number) => idx !== i);
                dispatch(updateField({ field: 'tags', value: newEtiquetas }));
              }}
              size="small"
            />
          ))}
          <Chip
            icon={<Iconify icon="mdi:plus" width={16} />}
            label="Agregar etiqueta"
            variant="outlined"
            size="small"
            onClick={() => {
              const tag = prompt('Ingresa etiqueta:');
              if (tag) dispatch(updateField({ field: 'tags', value: [...draft.tags, tag] }));
            }}
          />
        </Stack>
      </Box>
    </Stack>
  );
}

function TabSteps({ draft, dispatch, theme }: { draft: any; dispatch: any; theme: any }) {
  return (
    <Stack spacing={3}>
      <Alert severity="info" variant="outlined">
        Usa el planificador <strong>Sequential</strong> para ejecutar cadenas de prompts, decisiones y agregacion de tools en paralelo.
      </Alert>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="subtitle1" fontWeight={700}>
          Pasos del flujo del agente ({draft.steps.length})
        </Typography>
      </Box>

      {/* Add step buttons */}
      <Paper
        variant="outlined"
        sx={{ p: 2, display: 'flex', flexWrap: 'wrap', gap: 1 }}
      >
        {STEP_TYPES.map((st) => (
          <Button
            key={st.value}
            variant="outlined"
            size="small"
            startIcon={<Iconify icon={st.icon} />}
            sx={{
              borderColor: alpha(st.color, 0.5),
              color: st.color,
              '&:hover': { borderColor: st.color, bgcolor: alpha(st.color, 0.08) },
            }}
            onClick={() => {
              const newStep: AgentStep = {
                id: genId(),
                type: st.value,
                label: `${st.label} Step`,
                description: '',
                config: defaultConfigForStepType(st.value),
                position: { x: 0, y: draft.steps.length * 80 },
                connections: [],
              };
              dispatch(addStep(newStep));
            }}
          >
            {st.label}
          </Button>
        ))}
      </Paper>

      {/* Paso List */}
      {draft.steps.length === 0 && (
        <Alert severity="info" variant="outlined">
          Aun no hay pasos. Agrega pasos para definir el flujo del agente.
        </Alert>
      )}

      <Stack spacing={1.5}>
        {draft.steps.map((step: AgentStep, idx: number) => {
          const stepType = STEP_TYPES.find(s => s.value === step.type);
          return (
            <Paper
              key={step.id}
              variant="outlined"
              sx={{
                p: 2,
                borderLeft: `4px solid ${stepType?.color || '#999'}`,
                display: 'flex',
                alignItems: 'center',
                gap: 2,
                transition: 'all 0.2s',
                '&:hover': { boxShadow: theme.shadows[4] },
              }}
            >
              <Iconify
                icon={stepType?.icon || 'mdi:help'}
                width={28}
                sx={{ color: stepType?.color }}
              />
              <Box sx={{ flex: 1 }}>
                <TextField
                  size="small"
                  label="Nombre del paso"
                  value={step.label}
                  onChange={(e) => dispatch(updateStep({ id: step.id, changes: { label: e.target.value } }))}
                  sx={{ minWidth: 220 }}
                />
                <Typography variant="caption" color="text.secondary">
                  Paso {idx + 1} Â· {step.type.replace('_', ' ').toUpperCase()}
                </Typography>
                <TextField
                  size="small"
                  fullWidth
                  multiline
                  minRows={2}
                  label="Descripcion"
                  value={step.description}
                  onChange={(e) => dispatch(updateStep({ id: step.id, changes: { description: e.target.value } }))}
                  sx={{ mt: 1 }}
                />
                <TextField
                  size="small"
                  fullWidth
                  multiline
                  minRows={4}
                  label="Configuracion (JSON)"
                  value={JSON.stringify(step.config ?? {}, null, 2)}
                  onChange={(e) => {
                    try {
                      const next = JSON.parse(e.target.value || '{}');
                      dispatch(updateStep({ id: step.id, changes: { config: next } }));
                    } catch {
                      // Ignore partial invalid JSON while the user edits
                    }
                  }}
                  sx={{ mt: 1 }}
                />
              </Box>
        <Label color="default" variant="soft">{stepType?.label ?? step.type}</Label>
              <Tooltip title="Eliminar paso">
                <IconButton
                  size="small"
                  color="error"
                  onClick={() => dispatch(removeStep(step.id))}
                >
                  <Iconify icon="mdi:delete-outline" width={18} />
                </IconButton>
              </Tooltip>
            </Paper>
          );
        })}
      </Stack>
    </Stack>
  );
}

function TabGuardrails({ draft, dispatch }: { draft: any; dispatch: any }) {
  const theme = useTheme();
  const g = draft.guardrails;
  return (
    <Stack spacing={3}>
      <Typography variant="subtitle1" fontWeight={700}>Limites de ejecucion</Typography>
      <Stack direction="row" spacing={3}>
        <TextField
          label="Max Steps"
          type="number"
          value={g.maxSteps}
          onChange={(e) => dispatch(updateGuardrails({ maxSteps: Number(e.target.value) }))}
          sx={{ width: 160 }}
        />
        <TextField
          label="Timeout/Paso (ms)"
          type="number"
          value={g.timeoutPerStepMs}
          onChange={(e) => dispatch(updateGuardrails({ timeoutPerStepMs: Number(e.target.value) }))}
          sx={{ width: 180 }}
        />
        <TextField
          label="Max tokens"
          type="number"
          value={g.maxTokensPerExecution}
          onChange={(e) => dispatch(updateGuardrails({ maxTokensPerExecution: Number(e.target.value) }))}
          sx={{ width: 180 }}
        />
        <TextField
          label="Max reintentos"
          type="number"
          value={g.maxRetries}
          onChange={(e) => dispatch(updateGuardrails({ maxRetries: Number(e.target.value) }))}
          sx={{ width: 140 }}
        />
        <FormControl sx={{ width: 180 }}>
          <InputLabel>Planificador</InputLabel>
          <Select
            value={g.plannerType}
            label="Planificador"
            onChange={(e) => dispatch(updateGuardrails({ plannerType: e.target.value }))}
          >
            <MenuItem value="ReAct">ReAct</MenuItem>
            <MenuItem value="Sequential">Sequential</MenuItem>
            <MenuItem value="TreeOfThought">Tree of Thought</MenuItem>
          </Select>
        </FormControl>
        <FormControl sx={{ width: 180 }}>
          <InputLabel>Modo runtime</InputLabel>
          <Select
            value={g.runtimeMode}
            label="Modo runtime"
            onChange={(e) => dispatch(updateGuardrails({ runtimeMode: e.target.value }))}
          >
            <MenuItem value="Autonomous">Autonomous</MenuItem>
            <MenuItem value="Determinista">Determinista</MenuItem>
          </Select>
        </FormControl>
      </Stack>

      <Divider />
      <Typography variant="subtitle1" fontWeight={700}>Seguridad y gobierno</Typography>

      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box>
          <Typography variant="subtitle2">Tools en paralelo</Typography>
          <Typography variant="caption" color="text.secondary">Permite fan-out/fan-in cuando un paso de tool define varios nombres.</Typography>
        </Box>
        <Switch
          checked={g.allowParallelToolCalls}
          onChange={(e) => dispatch(updateGuardrails({ allowParallelToolCalls: e.target.checked }))}
        />
      </Box>

      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box>
          <Typography variant="subtitle2">Proteccion contra prompt injection</Typography>
          <Typography variant="caption" color="text.secondary">Escanea entradas para detectar ataques de inyeccion.</Typography>
        </Box>
        <Switch
          checked={g.enablePromptInjectionGuard}
          onChange={(e) => dispatch(updateGuardrails({ enablePromptInjectionGuard: e.target.checked }))}
        />
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box>
          <Typography variant="subtitle2">Proteccion de datos sensibles</Typography>
          <Typography variant="caption" color="text.secondary">Bloquea respuestas con datos sensibles.</Typography>
        </Box>
        <Switch
          checked={g.enablePIIProtection}
          onChange={(e) => dispatch(updateGuardrails({ enablePIIProtection: e.target.checked }))}
        />
      </Box>

      <Paper variant="outlined" sx={{ p: 2, bgcolor: alpha(theme.palette.info.main, 0.05) }}>
        <Stack spacing={2}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Box>
              <Typography variant="subtitle2" color="info.main">Humano en el loop (HITL)</Typography>
              <Typography variant="caption" color="text.secondary">Habilita checkpoints de revision manual.</Typography>
            </Box>
            <Switch
              checked={g.hitl.enabled}
              onChange={(e) => dispatch(updateGuardrails({ hitl: { ...g.hitl, enabled: e.target.checked } }))}
            />
          </Box>

          {g.hitl.enabled && (
             <Stack spacing={2} sx={{ pl: 2, borderLeft: `2px solid ${theme.palette.info.light}` }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <Typography variant="body2">Revisar todas las llamadas de tools</Typography>
                  <Switch 
                     size="small"
                     checked={g.hitl.requireReviewOnAllToolCalls}
                     onChange={(e) => dispatch(updateGuardrails({ hitl: { ...g.hitl, requireReviewOnAllToolCalls: e.target.checked } }))}
                  />
                </Box>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <Typography variant="body2">Revisar en escalamiento de politica</Typography>
                  <Switch 
                     size="small"
                     checked={g.hitl.requireReviewOnPolicyEscalation}
                     onChange={(e) => dispatch(updateGuardrails({ hitl: { ...g.hitl, requireReviewOnPolicyEscalation: e.target.checked } }))}
                  />
                </Box>
                <Box>
                  <Typography variant="body2" sx={{ mb: 1 }}>Umbral de confianza para omitir revision: {g.hitl.confidenceThreshold}</Typography>
                  <Slider
                    value={g.hitl.confidenceThreshold}
                    min={0}
                    max={1}
                    step={0.05}
                    onChange={(_, val) => dispatch(updateGuardrails({ hitl: { ...g.hitl, confidenceThreshold: val as number } }))}
                    valueLabelDisplay="auto"
                  />
                </Box>
             </Stack>
          )}
        </Stack>
      </Paper>
    </Stack>
  );
}

function TabMemory({ draft, dispatch }: { draft: any; dispatch: any }) {
  const m = draft.memory;
  return (
    <Stack spacing={2.5}>
      <Typography variant="subtitle1" fontWeight={700}>Configuracion de memoria</Typography>
      {([
        { key: 'workingMemory' as const, label: 'Memoria de trabajo', desc: 'Contexto de corto plazo para la ejecucion actual', icon: 'mdi:brain' },
        { key: 'longTermMemory' as const, label: 'Memoria de largo plazo', desc: 'Conocimiento persistente entre ejecuciones (MongoDB)', icon: 'mdi:database' },
        { key: 'vectorMemory' as const, label: 'Memoria vectorial', desc: 'Busqueda semantica con embeddings (Vector DB)', icon: 'mdi:vector-polyline' },
        { key: 'auditMemory' as const, label: 'Memoria de auditoria', desc: 'Bitacora inmutable de ejecucion (siempre activa)', icon: 'mdi:shield-check' },
      ]).map(({ key, label, desc, icon }) => (
        <Paper key={key} variant="outlined" sx={{ p: 2 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center' }}>
              <Iconify icon={icon} width={24} />
              <Box>
                <Typography variant="subtitle2">{label}</Typography>
                <Typography variant="caption" color="text.secondary">{desc}</Typography>
              </Box>
            </Box>
            <Switch
              checked={m[key]}
              disabled={key === 'auditMemory'}
              onChange={(e) => dispatch(updateMemory({ [key]: e.target.checked }))}
            />
          </Box>
        </Paper>
      ))}
    </Stack>
  );
}

function TabTools({
  draft,
  dispatch,
  availableTools,
  loading,
}: {
  draft: any;
  dispatch: any;
  availableTools: ToolOption[];
  loading: boolean;
}) {
  const selectedToolIds = new Set(draft.tools.map((t: { toolId: string }) => t.toolId));

  const bindTool = (tool: ToolOption) => {
    if (selectedToolIds.has(tool.extensionId)) return;

    dispatch(
      addTool({
        toolId: tool.extensionId,
        toolName: tool.name,
        version: tool.version,
        riskLevel: tool.riskLevel,
        permissions: [],
      })
    );
  };

  return (
    <Stack spacing={2.5}>
      <Typography variant="subtitle1" fontWeight={700}>Vinculacion de tools</Typography>
      <Typography variant="body2" color="text.secondary">
        Selecciona que tools de la plataforma puede ejecutar este agente.
      </Typography>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2" sx={{ mb: 1.5 }}>
          Tools vinculadas ({draft.tools.length})
        </Typography>
        {draft.tools.length === 0 ? (
          <Alert severity="warning" variant="outlined">
            Aun no hay tools vinculadas. Si el agente necesita tools, vincula al menos una.
          </Alert>
        ) : (
          <Stack spacing={1}>
            {draft.tools.map((tool: any) => (
              <Box
                key={tool.toolId}
                sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}
              >
                <Stack direction="row" spacing={1} alignItems="center">
                  <Typography variant="body2" fontWeight={600}>{tool.toolName}</Typography>
                  <Chip label={tool.riskLevel} size="small" variant="outlined" />
                </Stack>
                <IconButton size="small" color="error" onClick={() => dispatch(removeTool(tool.toolId))}>
                  <Iconify icon="mdi:delete-outline" width={18} />
                </IconButton>
              </Box>
            ))}
          </Stack>
        )}
      </Paper>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2" sx={{ mb: 1.5 }}>
          Tools disponibles en la plataforma
        </Typography>
        {loading ? (
          <Typography variant="body2" color="text.secondary">Cargando tools...</Typography>
        ) : availableTools.length === 0 ? (
          <Alert severity="info" variant="outlined">
            No se detectaron tools desde Extensions API.
          </Alert>
        ) : (
          <Stack spacing={1}>
            {availableTools.map((tool) => (
              <Box
                key={tool.extensionId}
                sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2 }}
              >
                <Box sx={{ minWidth: 0 }}>
                  <Typography variant="body2" fontWeight={600} noWrap>{tool.name}</Typography>
                  <Typography variant="caption" color="text.secondary" noWrap>
                    {tool.description || tool.extensionId}
                  </Typography>
                </Box>
                <Button
                  size="small"
                  variant="outlined"
                  disabled={selectedToolIds.has(tool.extensionId)}
                  onClick={() => bindTool(tool)}
                >
                  {selectedToolIds.has(tool.extensionId) ? 'Vinculada' : 'Vincular'}
                </Button>
              </Box>
            ))}
          </Stack>
        )}
      </Paper>
    </Stack>
  );
}

function TabModel({
  draft,
  dispatch,
  models,
  loading,
}: {
  draft: any;
  dispatch: any;
  models: ModelOption[];
  loading: boolean;
}) {
  const mc = draft.model;
  const providers = Array.from(new Set(models.map((m) => m.providerId)));
  const providerModels = models
    .filter((m) => m.providerId === mc.provider)
    .map((m) => m.modelId);
  const modelOptions = providerModels.length > 0 ? providerModels : FALLBACK_MODELS;

  return (
    <Stack spacing={3}>
      <Typography variant="subtitle1" fontWeight={700}>Configuracion del modelo IA</Typography>
      <Alert severity="info" variant="outlined">
        Los modelos se cargan desde Model Routing API. Si no esta disponible, se muestran opciones fallback.
      </Alert>
      {loading && (
        <Typography variant="body2" color="text.secondary">Cargando catalogo de modelos...</Typography>
      )}
      <FormControl fullWidth>
        <InputLabel>Proveedor</InputLabel>
        <Select
          value={mc.provider}
          label="Proveedor"
          onChange={(e) => dispatch(updateModel({ provider: e.target.value }))}
        >
          {(providers.length > 0 ? providers : ['OpenAI']).map((provider) => (
            <MenuItem key={provider} value={provider}>{provider}</MenuItem>
          ))}
        </Select>
      </FormControl>
      <Stack direction="row" spacing={3}>
        <FormControl fullWidth>
          <InputLabel>Modelo principal</InputLabel>
          <Select
            value={mc.primaryModel}
            label="Modelo principal"
            onChange={(e) => dispatch(updateModel({ primaryModel: e.target.value }))}
          >
            {modelOptions.map((m) => <MenuItem key={m} value={m}>{m}</MenuItem>)}
          </Select>
        </FormControl>
        <FormControl fullWidth>
          <InputLabel>Modelo de respaldo</InputLabel>
          <Select
            value={mc.fallbackModel}
            label="Modelo de respaldo"
            onChange={(e) => dispatch(updateModel({ fallbackModel: e.target.value }))}
          >
            {modelOptions.map((m) => <MenuItem key={m} value={m}>{m}</MenuItem>)}
          </Select>
        </FormControl>
      </Stack>

      <Box>
        <Typography variant="subtitle2" gutterBottom>
          Temperatura: {mc.temperature}
        </Typography>
        <Slider
          value={mc.temperature}
          min={0}
          max={2}
          step={0.1}
          onChange={(_, v) => dispatch(updateModel({ temperature: v as number }))}
          valueLabelDisplay="auto"
        />
        <Stack direction="row" justifyContent="space-between">
          <Typography variant="caption" color="text.secondary">Determinista</Typography>
          <Typography variant="caption" color="text.secondary">Creativo</Typography>
        </Stack>
      </Box>

      <TextField
        label="Max tokens de respuesta"
        type="number"
        value={mc.maxResponseTokens}
        onChange={(e) => dispatch(updateModel({ maxResponseTokens: Number(e.target.value) }))}
        sx={{ maxWidth: 250 }}
      />
    </Stack>
  );
}

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// MAIN PAGE
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

const TAB_LABELS = ['General', 'Flujo', 'Lienzo', 'Modo runtime', 'Memoria', 'Integraciones', 'Modelo IA'];
const TAB_ICONS = [
  'mdi:information-outline',
  'mdi:repeat',
  'mdi:sitemap',
  'mdi:shield-outline',
  'mdi:brain',
  'mdi:wrench-outline',
  'mdi:chip',
];

export default function AgentDesignerPage() {
  const dispatch = useDispatch<AppDispatch>();
  const { draft, activeTab, isDirty, saving, errors } = useSelector(
    (state: RootState) => state.designer
  );
  const theme = useTheme();
  const { agentId } = useParams<{ agentId: string }>();
  const [availableModels, setAvailableModels] = useState<ModelOption[]>([]);
  const [availableTools, setAvailableTools] = useState<ToolOption[]>([]);
  const [catalogLoading, setCatalogLoading] = useState(false);

  // Load agent when editing an existing one
  useEffect(() => {
    if (agentId) {
      dispatch(fetchAgentDetail(agentId));
    } else {
      dispatch(resetDraft());
    }
  }, [agentId, dispatch]);

  useEffect(() => {
    const loadCatalog = async () => {
      setCatalogLoading(true);
      try {
        const [modelsResponse, toolsResponse] = await Promise.allSettled([
          axios.get('/api/v1/model-routing/models'),
          axios.get('/api/v1/extensions/tools'),
        ]);

        if (modelsResponse.status === 'fulfilled' && Array.isArray(modelsResponse.value.data)) {
          setAvailableModels(
            modelsResponse.value.data.map((m: any) => ({
              modelId: m.modelId,
              providerId: m.providerId ?? 'OpenAI',
              displayName: m.displayName ?? m.modelId,
            }))
          );
        }

        if (toolsResponse.status === 'fulfilled' && Array.isArray(toolsResponse.value.data)) {
          setAvailableTools(
            toolsResponse.value.data.map((t: any) => ({
              extensionId: t.extensionId,
              name: t.name,
              version: t.version ?? '1.0.0',
              riskLevel: (t.riskLevel ?? 'Low') as ToolOption['riskLevel'],
              description: t.description ?? '',
            }))
          );
        }
      } finally {
        setCatalogLoading(false);
      }
    };

    loadCatalog();
  }, []);

  const handleSave = () => {
    dispatch(saveAgent(draft));
  };

  const handlePublish = () => {
    if (draft.id) {
      dispatch(publishAgent(draft.id));
    }
  };

  const renderTabContent = () => {
    switch (activeTab) {
      case 0:
        return <TabGeneral draft={draft} dispatch={dispatch} />;
      case 1:
        return <TabSteps draft={draft} dispatch={dispatch} theme={theme} />;
      case 2:
        return (
          <Stack spacing={2}>
            <Alert severity="info" variant="outlined">
              Canvas is a visual editor of step relationships. Modo runtime behavior is still enforced by engine policies and loop mode.
            </Alert>
            <AgentFlowCanvas steps={draft.steps} />
          </Stack>
        );
      case 3:
        return <TabGuardrails draft={draft} dispatch={dispatch} />;
      case 4:
        return <TabMemory draft={draft} dispatch={dispatch} />;
      case 5:
        return (
          <TabTools
            draft={draft}
            dispatch={dispatch}
            availableTools={availableTools}
            loading={catalogLoading}
          />
        );
      case 6:
        return (
          <TabModel
            draft={draft}
            dispatch={dispatch}
            models={availableModels}
            loading={catalogLoading}
          />
        );
      default:
        return null;
    }
  };

  return (
    <>
      <Helmet>
        <title>{draft.name || 'New Agent'} â€” Designer | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        {/* Error Banner */}
        {Object.keys(errors).length > 0 && (
          <Alert severity="error" sx={{ mb: 3 }}>
            {Object.values(errors).join(' Â· ')}
          </Alert>
        )}

        {/* Header */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 4 }}>
          <Box>
            <Typography variant="h4">Studio de Agente IA</Typography>
            <Typography variant="body2" color="text.secondary">
              {draft.name || 'Agente sin nombre'} Â· v{draft.version}
              {isDirty && (
                <Chip
                  label="Cambios sin guardar"
                  size="small"
                  color="warning"
                  variant="soft"
                  sx={{ ml: 1 }}
                />
              )}
              {saving && (
                <Chip label="Guardando..." size="small" color="info" variant="soft" sx={{ ml: 1 }} />
              )}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1.5}>
            <Button
              variant="outlined"
              color="inherit"
              startIcon={<Iconify icon="mdi:refresh" />}
              onClick={() => dispatch(resetDraft())}
            >
              Reiniciar
            </Button>
            <Button
              variant="contained"
              startIcon={<Iconify icon="mdi:content-save" />}
              disabled={!isDirty || saving}
              onClick={handleSave}
            >
              {saving ? 'Guardando...' : 'Guardar borrador'}
            </Button>
            <Button
              variant="contained"
              color="success"
              startIcon={<Iconify icon="mdi:rocket-launch" />}
              disabled={!draft.name || draft.steps.length === 0 || !draft.id || saving}
              onClick={handlePublish}
            >
              Publicar
            </Button>
          </Stack>
        </Box>

        {/* Tabs */}
        <Card
          sx={{
            border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
          }}
        >
          <Tabs
            value={activeTab}
            onChange={(_, v) => dispatch(setActiveTab(v))}
            sx={{
              px: 3,
              borderBottom: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
            }}
          >
            {TAB_LABELS.map((label, i) => (
              <Tab
                key={label}
                label={label}
                icon={<Iconify icon={TAB_ICONS[i]} width={20} />}
                iconPosition="start"
              />
            ))}
          </Tabs>
          <CardContent sx={{ p: 4 }}>{renderTabContent()}</CardContent>
        </Card>
      </DashboardContent>
    </>
  );
}


