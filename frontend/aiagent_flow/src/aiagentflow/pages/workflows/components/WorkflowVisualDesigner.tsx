import '@xyflow/react/dist/style.css';

import type { Edge, Node, NodeProps, Connection } from '@xyflow/react';

import { useMemo, useState, useEffect, useCallback } from 'react';
import {
  Handle,
  addEdge,
  MiniMap,
  Controls,
  Position,
  ReactFlow,
  Background,
  useEdgesState,
  useNodesState,
} from '@xyflow/react';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Tabs from '@mui/material/Tabs';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import MenuItem from '@mui/material/MenuItem';
import Checkbox from '@mui/material/Checkbox';
import Collapse from '@mui/material/Collapse';
import TextField from '@mui/material/TextField';
import Accordion from '@mui/material/Accordion';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import AccordionSummary from '@mui/material/AccordionSummary';
import AccordionDetails from '@mui/material/AccordionDetails';
import FormControlLabel from '@mui/material/FormControlLabel';

import { paths } from 'src/routes/paths';

import { Iconify } from 'src/components/iconify';

import {
  activityTypeLabel,
  ACTIVITY_TYPE_PRESETS,
  DEFAULT_AI_AGENT_CONFIG,
  ACTIVITY_TYPE_CATEGORY_ES,
} from '../constants';

import type {
  ToolOption,
  AgentOption,
  ModelOption,
  WorkflowStartIntent,
  WorkflowActivityNode,
  ConnectTemplateOption,
  WorkflowIntegrationStatus,
} from '../types';

type Props = {
  activities: WorkflowActivityNode[];
  allowedTypes: string[];
  requiredConfigByType: Record<string, string[]>;
  validationErrors: string[];
  triggerEventName: string;
  startIntents: WorkflowStartIntent[];
  availableModels: ModelOption[];
  availableTools: ToolOption[];
  availableAgents: AgentOption[];
  integrations: WorkflowIntegrationStatus[];
  connectTemplates: ConnectTemplateOption[];
  onAddActivity: (activityType?: string, patch?: Partial<WorkflowActivityNode>) => void;
  onChangeTriggerEvent: (value: string) => void;
  onUpdateStartIntents: (intents: WorkflowStartIntent[]) => void;
  onUpdateActivity: (index: number, patch: Partial<WorkflowActivityNode>) => void;
  onRemoveActivity: (index: number) => void;
  onOpenAiConfig: (index: number) => void;
  onUpdateAiAgentConfig: (index: number, patch: Partial<typeof DEFAULT_AI_AGENT_CONFIG>) => void;
  onAddActivityConfig: (index: number) => void;
  onUpdateActivityConfig: (index: number, key: string, value: string) => void;
  onRemoveActivityConfig: (index: number, key: string) => void;
};

type WorkflowNodeData = {
  label: string;
  activityType: string;
  description: string;
  badge?: string;
  index: number;
  onDuplicate?: () => void;
  onDelete?: () => void;
};

type WorkflowStartNodeData = {
  triggerEventName: string;
  intents: WorkflowStartIntent[];
};

type DesignerNodeData = WorkflowNodeData | WorkflowStartNodeData;
type DesignerNode = Node<DesignerNodeData>;

const activityDescription = (type: string) => {
  if (type === 'ai.agent') return 'Decide, responde o usa herramientas';
  if (type === 'connect.send_whatsapp_template') return 'Envia una plantilla aprobada';
  if (type === 'connect.enqueue_campaign_message') return 'Programa un mensaje saliente';
  if (type === 'connect.update_inbox_status') return 'Actualiza la bandeja de entrada';
  if (type.startsWith('human.')) return 'Escala o asigna a un equipo';
  if (type.startsWith('kyc.')) return 'Valida identidad o revision';
  if (type.startsWith('payments.')) return 'Crea o gestiona pagos';
  return 'Accion del flujo';
};

const extractTemplateVariables = (body: string) =>
  Array.from(body.matchAll(/\{\{\s*([^}]+?)\s*\}\}/g))
    .map((match) => match[1]?.trim())
    .filter(Boolean);

const nodeColorByType = (type: string) => {
  if (type === 'ai.agent') return '#2667ff';
  if (type.startsWith('connect.')) return '#0ea5a3';
  if (type.startsWith('kyc.')) return '#f59e0b';
  if (type.startsWith('payments.')) return '#7c3aed';
  return '#64748b';
};

const dockItems = [
  { label: 'AI Agent', icon: 'mdi:brain', types: ['ai.agent'] },
  { label: 'WhatsApp', icon: 'mdi:whatsapp', types: ['connect.send_whatsapp_template'] },
  { label: 'Mensaje', icon: 'mdi:message-text-outline', types: ['connect.enqueue_campaign_message', 'connect.update_inbox_status'] },
  { label: 'API', icon: 'mdi:api', types: ['webhook.call', 'http.request'] },
  { label: 'Code', icon: 'mdi:code-tags', types: ['code.execute'] },
  { label: 'Logica', icon: 'mdi:source-branch', types: ['condition', 'logic.condition'] },
  { label: 'Bases de datos', icon: 'mdi:database-outline', types: ['database.query'] },
  { label: 'Notas', icon: 'mdi:note-outline', types: ['note.create'] },
  { label: 'Pagos', icon: 'mdi:cash', types: ['payments.create_intent'] },
  { label: 'Atencion Humana', icon: 'mdi:account-headset', types: ['human.assign', 'human.handoff'] },
  { label: 'KYC', icon: 'mdi:card-account-details-outline', types: ['kyc.document_check', 'kyc.review_case'] },
];

function WorkflowNodeCard({ data, selected }: NodeProps<Node<WorkflowNodeData>>) {
  const activityType = String(data.activityType ?? '');
  const label = String(data.label ?? activityType);
  const description = String(data.description ?? activityDescription(activityType));
  const color = nodeColorByType(activityType);
  const category = activityType.split('.')[0]?.toUpperCase() ?? 'STEP';

  return (
    <Box
      sx={{
        border: `1px solid ${selected ? color : '#d0d5dd'}`,
        borderRadius: 2,
        background: '#ffffff',
        boxShadow: selected ? `0 0 0 2px ${color}33` : '0 1px 2px rgba(16,24,40,0.08)',
        p: 1,
        minWidth: 220,
      }}
    >
      <Handle type="target" position={Position.Left} id="in" />
      <Stack spacing={0.4}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Typography variant="caption" sx={{ color, fontWeight: 700 }}>
            {ACTIVITY_TYPE_CATEGORY_ES[activityType.split('.')[0] ?? 'other'] ?? category}
          </Typography>
          <Stack direction="row" spacing={0.4}>
            <IconButton size="small" onClick={() => data.onDuplicate?.()}>
              <Iconify width={14} icon="mdi:content-copy" />
            </IconButton>
            <IconButton size="small" color="error" onClick={() => data.onDelete?.()}>
              <Iconify width={14} icon="mdi:close" />
            </IconButton>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ wordBreak: 'break-word' }}>
          {description}
        </Typography>
        {data.badge && <Chip size="small" label={String(data.badge)} sx={{ alignSelf: 'flex-start' }} />}
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

function StartWorkflowNode({ data, selected }: NodeProps<Node<WorkflowStartNodeData>>) {
  return (
    <Box
      sx={{
        border: `1px solid ${selected ? '#00acc1' : '#00b8d9'}`,
        borderRadius: 2,
        background: '#f0fdff',
        boxShadow: selected ? '0 0 0 2px #67e8f966' : '0 1px 2px rgba(16,24,40,0.08)',
        p: 1,
        minWidth: 230,
      }}
    >
      <Stack spacing={0.7}>
        <Stack direction="row" alignItems="center" spacing={0.7}>
          <Iconify icon="mdi:flag-outline" width={17} color="#00a6bd" />
          <Typography variant="body2" sx={{ fontWeight: 800, color: '#03768a' }}>
            Inicio
          </Typography>
        </Stack>
        <Typography variant="caption" color="text.secondary">
          Trigger: {data.triggerEventName}
        </Typography>
        <Stack spacing={0.5}>
          {data.intents.slice(0, 3).map((intent) => (
            <Box key={intent.id} sx={{ px: 0.8, py: 0.5, borderRadius: 1, bgcolor: '#fff' }}>
              <Typography variant="caption" sx={{ fontWeight: 700 }}>
                {intent.label}
              </Typography>
            </Box>
          ))}
        </Stack>
        <Button size="small" variant="contained" sx={{ borderRadius: 3, bgcolor: '#00acc1' }}>
          + Agregar Intencion
        </Button>
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ background: '#00acc1' }} />
    </Box>
  );
}

function AiWorkflowNode({ data, selected }: NodeProps<Node<WorkflowNodeData>>) {
  const label = String(data.label ?? 'AI Agent');
  const model = String(data.badge ?? 'modelo pendiente');
  return (
    <Box
      sx={{
        border: `1px solid ${selected ? '#1d4ed8' : '#bfdbfe'}`,
        borderRadius: 2,
        background: 'linear-gradient(180deg, #eff6ff 0%, #ffffff 50%)',
        boxShadow: selected ? '0 0 0 2px #93c5fd66' : '0 1px 2px rgba(16,24,40,0.08)',
        p: 1,
        minWidth: 230,
      }}
    >
      <Handle type="target" position={Position.Left} id="in" />
      <Stack spacing={0.4}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Typography variant="caption" sx={{ color: '#1d4ed8', fontWeight: 700 }}>
            AI
          </Typography>
          <Stack direction="row" spacing={0.4}>
            <IconButton size="small" onClick={() => data.onDuplicate?.()}>
              <Iconify width={14} icon="mdi:content-copy" />
            </IconButton>
            <IconButton size="small" color="error" onClick={() => data.onDelete?.()}>
              <Iconify width={14} icon="mdi:close" />
            </IconButton>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          Agente configurado para decidir y ejecutar tools
        </Typography>
        <Chip size="small" color="primary" variant="soft" label={model} sx={{ alignSelf: 'flex-start' }} />
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

function ConnectWorkflowNode({ data, selected }: NodeProps<Node<WorkflowNodeData>>) {
  const label = String(data.label ?? 'Connect');
  const badge = data.badge ? String(data.badge) : 'canal';
  return (
    <Box
      sx={{
        border: `1px solid ${selected ? '#0f766e' : '#99f6e4'}`,
        borderRadius: 2,
        background: 'linear-gradient(180deg, #f0fdfa 0%, #ffffff 50%)',
        boxShadow: selected ? '0 0 0 2px #2dd4bf55' : '0 1px 2px rgba(16,24,40,0.08)',
        p: 1,
        minWidth: 230,
      }}
    >
      <Handle type="target" position={Position.Left} id="in" />
      <Stack spacing={0.4}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Typography variant="caption" sx={{ color: '#0f766e', fontWeight: 700 }}>
            CONNECT
          </Typography>
          <Stack direction="row" spacing={0.4}>
            <IconButton size="small" onClick={() => data.onDuplicate?.()}>
              <Iconify width={14} icon="mdi:content-copy" />
            </IconButton>
            <IconButton size="small" color="error" onClick={() => data.onDelete?.()}>
              <Iconify width={14} icon="mdi:close" />
            </IconButton>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {String(data.description ?? 'envios y estado')}
        </Typography>
        <Chip size="small" label={badge} sx={{ alignSelf: 'flex-start' }} />
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

function HumanWorkflowNode({ data, selected }: NodeProps<Node<WorkflowNodeData>>) {
  const label = String(data.label ?? 'Human');
  return (
    <Box
      sx={{
        border: `1px solid ${selected ? '#7c2d12' : '#fdba74'}`,
        borderRadius: 2,
        background: 'linear-gradient(180deg, #fff7ed 0%, #ffffff 50%)',
        boxShadow: selected ? '0 0 0 2px #fb923c55' : '0 1px 2px rgba(16,24,40,0.08)',
        p: 1,
        minWidth: 230,
      }}
    >
      <Handle type="target" position={Position.Left} id="in" />
      <Stack spacing={0.4}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Typography variant="caption" sx={{ color: '#7c2d12', fontWeight: 700 }}>
            HUMAN
          </Typography>
          <Stack direction="row" spacing={0.4}>
            <IconButton size="small" onClick={() => data.onDuplicate?.()}>
              <Iconify width={14} icon="mdi:content-copy" />
            </IconButton>
            <IconButton size="small" color="error" onClick={() => data.onDelete?.()}>
              <Iconify width={14} icon="mdi:close" />
            </IconButton>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          escalacion y asignacion
        </Typography>
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

const toNodes = (
  activities: WorkflowActivityNode[],
  triggerEventName: string,
  startIntents: WorkflowStartIntent[],
  onDuplicate: (index: number) => void,
  onDelete: (index: number) => void
): DesignerNode[] => [
  {
    id: '__start__',
    position: { x: 60, y: 130 },
    data: {
      triggerEventName,
      intents: startIntents,
    },
    type: 'startNode',
  },
  ...activities.map((activity, idx) => ({
    id: activity.id || `step-${idx + 1}`,
    position: activity.position ?? { x: 360 + (idx % 3) * 300, y: 120 + Math.floor(idx / 3) * 190 },
    data: {
      label: activity.name || activity.id || activityTypeLabel(activity.type),
      activityType: activity.type,
      description: activityDescription(activity.type),
      badge: activity.type === 'ai.agent'
        ? activity.aiAgent?.agentName || activity.config?.agentName || activity.config?.agentId
        : activity.config?.channel || activity.config?.status,
      index: idx,
      onDuplicate: () => onDuplicate(idx),
      onDelete: () => onDelete(idx),
    },
    type: activity.type === 'ai.agent'
      ? 'aiNode'
      : activity.type.startsWith('connect.')
        ? 'connectNode'
        : activity.type.startsWith('human.')
          ? 'humanNode'
          : 'workflowNode',
  })),
];

const toEdges = (activities: WorkflowActivityNode[]): Edge[] => {
  const byId = new Map(activities.map((a) => [a.id, a]));
  const edges: Edge[] = [];

  activities.forEach((activity, idx) => {
    const source = activity.id || `step-${idx + 1}`;
    const targets = [
      { key: activity.next, label: 'next' },
      { key: activity.onSuccess, label: 'ok' },
      { key: activity.onFailure, label: 'fail' },
    ].filter((x) => !!x.key && byId.has(x.key!));

    targets.forEach((target, tIdx) => {
      edges.push({
        id: `${source}-${target.key}-${target.label}-${tIdx}`,
        source,
        target: target.key!,
        label: target.label,
        sourceHandle: target.label === 'ok' ? 'success' : target.label === 'fail' ? 'failure' : 'next',
        markerEnd: { type: 'arrowclosed' },
      });
    });
  });

  const first = activities[0];
  if (first) {
    edges.unshift({
      id: `__start__-${first.id || 'step-1'}`,
      source: '__start__',
      target: first.id || 'step-1',
      label: 'inicio',
      sourceHandle: 'next',
      markerEnd: { type: 'arrowclosed' },
      style: { stroke: '#cbd5e1', strokeWidth: 2 },
    });
  }

  return edges;
};

const graphLayout = (activities: WorkflowActivityNode[]): Record<string, { x: number; y: number }> => {
  const byId = new Map(activities.map((a, idx) => [a.id || `step-${idx + 1}`, a]));
  const out = new Map<string, string[]>();
  const indegree = new Map<string, number>();
  byId.forEach((_, id) => {
    out.set(id, []);
    indegree.set(id, 0);
  });

  activities.forEach((a, idx) => {
    const id = a.id || `step-${idx + 1}`;
    const targets = [a.next, a.onSuccess, a.onFailure].filter(Boolean) as string[];
    targets.forEach((t) => {
      if (!byId.has(t)) return;
      out.get(id)?.push(t);
      indegree.set(t, (indegree.get(t) ?? 0) + 1);
    });
  });

  const roots = Array.from(indegree.entries())
    .filter(([, d]) => d === 0)
    .map(([id]) => id);
  const queue = roots.length > 0 ? [...roots] : Array.from(byId.keys()).slice(0, 1);

  const layer = new Map<string, number>();
  queue.forEach((id) => layer.set(id, 0));

  while (queue.length > 0) {
    const current = queue.shift()!;
    const currentLayer = layer.get(current) ?? 0;
    (out.get(current) ?? []).forEach((next) => {
      const proposed = currentLayer + 1;
      if ((layer.get(next) ?? -1) < proposed) layer.set(next, proposed);
      indegree.set(next, (indegree.get(next) ?? 1) - 1);
      if ((indegree.get(next) ?? 0) <= 0) queue.push(next);
    });
  }

  byId.forEach((_, id) => {
    if (!layer.has(id)) layer.set(id, 0);
  });

  const columns = new Map<number, string[]>();
  layer.forEach((l, id) => {
    const arr = columns.get(l) ?? [];
    arr.push(id);
    columns.set(l, arr);
  });

  const result: Record<string, { x: number; y: number }> = {};
  Array.from(columns.entries())
    .sort((a, b) => a[0] - b[0])
    .forEach(([col, ids]) => {
      ids.sort();
      ids.forEach((id, row) => {
        result[id] = { x: 120 + col * 320, y: 100 + row * 180 };
      });
    });

  return result;
};

export function WorkflowVisualDesigner({
  activities,
  allowedTypes,
  requiredConfigByType,
  validationErrors,
  triggerEventName,
  startIntents,
  availableModels,
  availableTools,
  availableAgents,
  integrations,
  connectTemplates,
  onAddActivity,
  onChangeTriggerEvent,
  onUpdateStartIntents,
  onUpdateActivity,
  onRemoveActivity,
  onOpenAiConfig,
  onUpdateAiAgentConfig,
  onAddActivityConfig,
  onUpdateActivityConfig,
  onRemoveActivityConfig,
}: Props) {
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [aiTab, setAiTab] = useState(0);
  const [showValidation, setShowValidation] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [inspectorSection, setInspectorSection] = useState<string>('general');
  const [nodes, setNodes, onNodesChange] = useNodesState<DesignerNode>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);

  const duplicateNodeAt = useCallback((index: number) => {
    const source = activities[index];
    if (!source) return;
    const nextIndex = activities.length;
    const newId = `${source.id || 'step'}_copy_${Date.now()}`;
    onAddActivity(source.type, {
      ...source,
      id: newId,
      name: source.name ? `${source.name} (copy)` : undefined,
      position: source.position ? { x: source.position.x + 40, y: source.position.y + 40 } : undefined,
    });
    setSelectedIndex(nextIndex);
  }, [activities, onAddActivity]);

  const deleteNodeAt = useCallback((index: number) => {
    const target = activities[index];
    if (!target) return;
    onRemoveActivity(index);
    activities.forEach((activity, i) => {
      if (i === index) return;
      const patch: Partial<WorkflowActivityNode> = {};
      if (activity.next === target.id) patch.next = undefined;
      if (activity.onSuccess === target.id) patch.onSuccess = undefined;
      if (activity.onFailure === target.id) patch.onFailure = undefined;
      if (Object.keys(patch).length > 0) onUpdateActivity(i, patch);
    });
    setSelectedIndex(null);
  }, [activities, onRemoveActivity, onUpdateActivity]);

  useEffect(() => {
    setNodes(toNodes(activities, triggerEventName, startIntents, duplicateNodeAt, deleteNodeAt));
    setEdges(toEdges(activities));
  }, [activities, deleteNodeAt, duplicateNodeAt, setEdges, setNodes, startIntents, triggerEventName]);

  const selected = useMemo(
    () => (selectedIndex !== null && selectedIndex >= 0 ? activities[selectedIndex] : null),
    [activities, selectedIndex]
  );
  const selectedStart = selectedIndex === -1;
  const requiredKeys = selected ? requiredConfigByType[selected.type] ?? [] : [];
  const handleSelectedTypeChange = (type: string) => {
    if (selectedIndex === null) return;
    onUpdateActivity(selectedIndex, {
      type,
      name: activityTypeLabel(type),
      config: { ...(ACTIVITY_TYPE_PRESETS[type] ?? {}) },
      aiAgent: type === 'ai.agent' ? { ...DEFAULT_AI_AGENT_CONFIG } : undefined,
    });
    setInspectorSection(type === 'ai.agent' ? 'ia' : 'general');
  };
  const selectedIntegration = useMemo(() => {
    if (!selected?.type.startsWith('connect.')) return null;
    const channel = (selected.config?.channel ?? '').toLowerCase();
    if (channel) {
      const byChannel = integrations.find((x) => x.category === 'channel' && x.key === `channel:${channel}`);
      if (byChannel) return byChannel;
    }
    return integrations.find((x) => x.category === 'channel') ?? null;
  }, [integrations, selected]);
  const selectedTemplate = useMemo(
    () =>
      selected?.config?.templateId
        ? connectTemplates.find((template) => template.id === selected.config?.templateId) ?? null
        : null,
    [connectTemplates, selected]
  );
  const selectedTemplateVariables = useMemo(
    () => extractTemplateVariables(selectedTemplate?.body ?? selected?.config?.content ?? ''),
    [selected, selectedTemplate]
  );
  const publishedAgents = useMemo(
    () => availableAgents.filter((agent) => agent.status === 'Published'),
    [availableAgents]
  );
  const workflowContextKeywords = useMemo(
    () =>
      [
        triggerEventName,
        ...startIntents.flatMap((intent) => [intent.label, intent.description, intent.eventName, ...(intent.examples ?? [])]),
      ]
        .join(' ')
        .toLowerCase(),
    [startIntents, triggerEventName]
  );
  const recommendedAgentIds = useMemo(
    () =>
      new Set(
        publishedAgents
          .filter((agent) =>
            (agent.tags ?? []).some((tag) => {
              const normalized = tag.toLowerCase();
              return normalized.length > 2 && workflowContextKeywords.includes(normalized);
            })
          )
          .map((agent) => agent.id)
      ),
    [publishedAgents, workflowContextKeywords]
  );
  const visibleAgents = useMemo(
    () =>
      [...publishedAgents].sort((left, right) => {
        const leftRecommended = recommendedAgentIds.has(left.id) ? 0 : 1;
        const rightRecommended = recommendedAgentIds.has(right.id) ? 0 : 1;
        return leftRecommended - rightRecommended || left.name.localeCompare(right.name);
      }),
    [publishedAgents, recommendedAgentIds]
  );
  const selectedAgent = useMemo(() => {
    const agentId = selected?.config?.agentId ?? selected?.aiAgent?.agentId;
    return agentId ? availableAgents.find((agent) => agent.id === agentId) ?? null : null;
  }, [availableAgents, selected]);
  const selectPublishedAgent = (agentId: string) => {
    if (selectedIndex === null || !selected) return;
    const agent = availableAgents.find((item) => item.id === agentId);
    onUpdateActivity(selectedIndex, {
      name: agent?.name || selected.name || activityTypeLabel('ai.agent'),
      aiAgent: {
        ...(selected.aiAgent ?? DEFAULT_AI_AGENT_CONFIG),
        agentId,
        agentName: agent?.name ?? '',
        agentVersion: agent?.version ?? '',
      },
      config: {
        ...(selected.config ?? {}),
        agentId,
        agentName: agent?.name ?? '',
        agentVersion: agent?.version ? String(agent.version) : '',
        input: selected.config?.input ?? selected.aiAgent?.input ?? DEFAULT_AI_AGENT_CONFIG.input ?? '{{payload.content}}',
        context: selected.config?.context ?? selected.aiAgent?.context ?? '{{payload.channel}}',
      },
    });
  };

  const availableDockItems = useMemo(
    () =>
      dockItems
        .map((item) => ({ ...item, type: item.types.find((type) => allowedTypes.includes(type)) }))
        .filter((item) => item.types.length > 0),
    [allowedTypes]
  );

  const onConnect = (params: Connection) => {
    const sourceIndex = activities.findIndex((a) => a.id === params.source);
    if (sourceIndex < 0 || !params.target) return;
    if (params.sourceHandle === 'success') onUpdateActivity(sourceIndex, { onSuccess: params.target });
    else if (params.sourceHandle === 'failure') onUpdateActivity(sourceIndex, { onFailure: params.target });
    else onUpdateActivity(sourceIndex, { next: params.target });
    setEdges((prev) => addEdge({ ...params, markerEnd: { type: 'arrowclosed' } }, prev));
  };

  const applyAutoLayoutGrid = () => {
    activities.forEach((activity, idx) => {
      onUpdateActivity(idx, {
        position: { x: 120 + (idx % 3) * 280, y: 100 + Math.floor(idx / 3) * 180 },
      });
    });
  };

  const applyAutoLayoutGraph = () => {
    const positions = graphLayout(activities);
    activities.forEach((activity, idx) => {
      const id = activity.id || `step-${idx + 1}`;
      if (!positions[id]) return;
      onUpdateActivity(idx, { position: positions[id] });
    });
  };

  const addByType = (type: string) => {
    const index = activities.length;
    onAddActivity(type, {
      id: `step-${Date.now()}`,
      name: activityTypeLabel(type),
      aiAgent: type === 'ai.agent' ? { ...DEFAULT_AI_AGENT_CONFIG } : undefined,
    });
    setSelectedIndex(index);
    setInspectorSection(type === 'ai.agent' ? 'ia' : 'general');
  };

  return (
    <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2, boxShadow: '0 2px 10px rgba(15,23,42,0.06)' }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
        <Typography variant="subtitle1">Brain Studio</Typography>
        <Stack direction="row" spacing={1}>
          <Button size="small" variant="outlined" onClick={() => setShowValidation(true)}>Validaciones</Button>
          <Tooltip title="Auto layout por grafo">
            <IconButton size="small" onClick={applyAutoLayoutGraph}><Iconify icon="mdi:graph-outline" /></IconButton>
          </Tooltip>
          <Tooltip title="Auto layout en grilla">
            <IconButton size="small" onClick={applyAutoLayoutGrid}><Iconify icon="mdi:grid" /></IconButton>
          </Tooltip>
          <Button size="small" onClick={() => onAddActivity()} startIcon={<Iconify icon="mingcute:add-line" />}>Nodo basico</Button>
        </Stack>
      </Stack>

      <Box
        sx={{
          position: 'relative',
          height: 650,
          border: '1px solid',
          borderColor: 'divider',
          borderRadius: 1.5,
          overflow: 'hidden',
          bgcolor: '#fff',
        }}
      >
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={{
            startNode: StartWorkflowNode,
            workflowNode: WorkflowNodeCard,
            aiNode: AiWorkflowNode,
            connectNode: ConnectWorkflowNode,
            humanNode: HumanWorkflowNode,
          }}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          onNodeDragStop={(_, node) => {
            if (node.id === '__start__') return;
            const index = Number('index' in node.data ? node.data.index : -1);
            if (Number.isNaN(index) || index < 0) return;
            onUpdateActivity(index, { position: node.position });
          }}
          onNodeClick={(_, node) => {
            if (node.id === '__start__') {
              setSelectedIndex(-1);
              return;
            }
            const index = Number('index' in node.data ? node.data.index : -1);
            setSelectedIndex(index);
            const activity = activities[index];
            setInspectorSection(activity?.type === 'ai.agent' ? 'ia' : 'general');
          }}
          fitView
        >
          <MiniMap />
          <Controls />
          <Background gap={18} size={1} color="#d9dee8" />
        </ReactFlow>

        <Stack
          direction="row"
          spacing={0.8}
          sx={{
            position: 'absolute',
            left: '50%',
            bottom: 18,
            zIndex: 5,
            maxWidth: 'calc(100% - 48px)',
            overflowX: 'auto',
            transform: 'translateX(-50%)',
            p: 0.8,
            borderRadius: 2,
            bgcolor: 'rgba(255,255,255,0.96)',
            boxShadow: '0 12px 32px rgba(15,23,42,0.14)',
            border: '1px solid #e5e7eb',
          }}
        >
          {availableDockItems.map((item) => {
            const disabled = !item.type;
            return (
              <Tooltip key={item.label} title={item.type ? activityTypeLabel(item.type) : 'Proximo modulo'}>
                <span>
                  <Button
                    disabled={disabled}
                    onClick={() => {
                      if (item.type) addByType(item.type);
                    }}
                    sx={{
                      minWidth: 78,
                      height: 62,
                      px: 1,
                      color: '#0f172a',
                      borderRadius: 1.5,
                      opacity: disabled ? 0.55 : 1,
                      textTransform: 'none',
                    }}
                  >
                    <Stack alignItems="center" spacing={0.4}>
                      <Iconify icon={item.icon} width={20} />
                      <Typography variant="caption" sx={{ lineHeight: 1.1, fontWeight: 600 }}>
                        {item.label}
                      </Typography>
                    </Stack>
                  </Button>
                </span>
              </Tooltip>
            );
          })}
        </Stack>
      </Box>

      <Drawer
        anchor="right"
        open={selected !== null || selectedStart}
        onClose={() => setSelectedIndex(null)}
        PaperProps={{ sx: { width: 420, p: 2 } }}
      >
        {selectedStart && (
          <Stack spacing={1.4}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Box>
                <Typography variant="subtitle1">Inicio del workflow</Typography>
                <Typography variant="caption" color="text.secondary">
                  Las intenciones son formas entendibles de arrancar el flujo; internamente disparan un evento.
                </Typography>
              </Box>
              <Chip size="small" label="Trigger" />
            </Stack>
            <Divider />
            <TextField
              label="Evento interno"
              value={triggerEventName}
              onChange={(e) => onChangeTriggerEvent(e.target.value)}
              size="small"
              helperText="Ejemplo: connect.message.received. El backend ejecuta workflows publicados con este evento."
            />
            <Alert severity="info">
              Un usuario sin conocimiento tecnico solo define intenciones con nombre y ejemplos. El sistema las usa para
              mapear mensajes, botones o webhooks al evento interno.
            </Alert>
            <Stack spacing={1}>
              {startIntents.map((intent, index) => (
                <Card key={intent.id} variant="outlined" sx={{ p: 1 }}>
                  <Stack spacing={1}>
                    <TextField
                      label="Nombre de la intencion"
                      value={intent.label}
                      size="small"
                      onChange={(e) => {
                        const next = [...startIntents];
                        next[index] = { ...intent, label: e.target.value };
                        onUpdateStartIntents(next);
                      }}
                    />
                    <TextField
                      label="Descripcion"
                      value={intent.description ?? ''}
                      size="small"
                      onChange={(e) => {
                        const next = [...startIntents];
                        next[index] = { ...intent, description: e.target.value };
                        onUpdateStartIntents(next);
                      }}
                    />
                    <TextField
                      label="Ejemplos que diria el usuario"
                      value={(intent.examples ?? []).join(', ')}
                      size="small"
                      helperText="Separados por coma"
                      onChange={(e) => {
                        const next = [...startIntents];
                        next[index] = {
                          ...intent,
                          examples: e.target.value
                            .split(',')
                            .map((value) => value.trim())
                            .filter(Boolean),
                        };
                        onUpdateStartIntents(next);
                      }}
                    />
                    <Button
                      color="error"
                      size="small"
                      disabled={startIntents.length === 1}
                      onClick={() => onUpdateStartIntents(startIntents.filter((x) => x.id !== intent.id))}
                    >
                      Eliminar intencion
                    </Button>
                  </Stack>
                </Card>
              ))}
            </Stack>
            <Button
              variant="contained"
              onClick={() =>
                onUpdateStartIntents([
                  ...startIntents,
                  {
                    id: `intent-${Date.now()}`,
                    label: 'Nueva intencion',
                    description: 'Describe cuando debe iniciar este flujo.',
                    examples: [],
                    eventName: triggerEventName,
                  },
                ])
              }
            >
              Agregar intencion
            </Button>
          </Stack>
        )}
        {selected && selectedIndex !== null && (
          <Stack spacing={1}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Box>
                <Typography variant="subtitle1">{selected.name || activityTypeLabel(selected.type)}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {selected.type === 'ai.agent'
                    ? 'Selecciona un agente publicado y define que datos del flujo recibe.'
                    : 'Configura solo lo esencial. Lo tecnico esta en Runtime y Debug.'}
                </Typography>
              </Box>
              <Chip size="small" label={activityTypeLabel(selected.type)} />
            </Stack>
            <Divider />
            {selected.type === 'ai.agent' && (
              <Card variant="outlined" sx={{ p: 1.2, bgcolor: '#fbfdff' }}>
                <Stack spacing={1}>
                  <TextField
                    label="Agente publicado"
                    select
                    value={selected.config?.agentId ?? selected.aiAgent?.agentId ?? ''}
                    helperText="El modelo, tools, memoria y guardrails viven en Agent Studio."
                    onChange={(e) => selectPublishedAgent(e.target.value)}
                    size="small"
                  >
                    <MenuItem value="">Seleccionar agente</MenuItem>
                    {visibleAgents.map((agent) => (
                      <MenuItem key={agent.id} value={agent.id}>
                        {agent.name}
                        {recommendedAgentIds.has(agent.id) ? ' (recomendado)' : ''}
                      </MenuItem>
                    ))}
                  </TextField>
                  {publishedAgents.length === 0 && (
                    <Alert severity="warning">
                      No hay agentes publicados para usar en el flujo. Publica uno desde Agent Studio.
                    </Alert>
                  )}
                  {selectedAgent && (
                    <Box sx={{ p: 1, borderRadius: 1, border: '1px solid #bae6fd', bgcolor: '#f0f9ff' }}>
                      <Stack direction="row" spacing={0.6} alignItems="center" flexWrap="wrap">
                        <Typography variant="caption" fontWeight={700}>
                          {selectedAgent.name}
                        </Typography>
                        <Chip size="small" label={selectedAgent.status === 'Published' ? 'Publicado' : selectedAgent.status} />
                        {selectedAgent.version && <Chip size="small" label={`v${selectedAgent.version}`} />}
                      </Stack>
                      {selectedAgent.description && (
                        <Typography variant="caption" display="block" color="text.secondary" sx={{ mt: 0.5 }}>
                          {selectedAgent.description}
                        </Typography>
                      )}
                      {selectedAgent.tags && selectedAgent.tags.length > 0 && (
                        <Stack direction="row" spacing={0.5} flexWrap="wrap" sx={{ mt: 0.8 }}>
                          {selectedAgent.tags.map((tag) => (
                            <Chip key={tag} size="small" variant="outlined" label={tag} />
                          ))}
                        </Stack>
                      )}
                      <Stack direction="row" spacing={0.7} flexWrap="wrap" sx={{ mt: 0.8 }}>
                        <Chip size="small" label={`${selectedAgent.stepsCount ?? 0} pasos`} />
                        <Chip size="small" label={`${selectedAgent.toolsCount ?? 0} tools`} />
                        {selectedAgent.primaryModel && <Chip size="small" label={selectedAgent.primaryModel} />}
                      </Stack>
                      <Button
                        size="small"
                        variant="outlined"
                        href={paths.dashboard.agentEdit(selectedAgent.id)}
                        sx={{ mt: 1 }}
                      >
                        Editar subflujo del agente
                      </Button>
                    </Box>
                  )}
                </Stack>
              </Card>
            )}
            {selected.type !== 'ai.agent' && (
            <Accordion expanded={inspectorSection === 'general'} onChange={(_, e) => setInspectorSection(e ? 'general' : '')}>
              <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" />}>
                <Typography variant="subtitle2">General</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <Stack spacing={1}>
                  <TextField
                    label="Tipo de nodo"
                    select
                    value={selected.type}
                    onChange={(e) => handleSelectedTypeChange(e.target.value)}
                    size="small"
                  >
                    {allowedTypes.map((type) => (
                      <MenuItem key={type} value={type}>
                        {activityTypeLabel(type)}
                      </MenuItem>
                    ))}
                  </TextField>
                  <TextField
                    label="Nombre"
                    value={selected.name ?? ''}
                    onChange={(e) => onUpdateActivity(selectedIndex, { name: e.target.value || undefined })}
                    size="small"
                  />
                </Stack>
              </AccordionDetails>
            </Accordion>
            )}
            {(selected.type.startsWith('connect.') || selected.type.startsWith('human.')) && (
              <Accordion expanded={inspectorSection === 'integraciones'} onChange={(_, e) => setInspectorSection(e ? 'integraciones' : '')}>
                <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" />}>
                  <Typography variant="subtitle2">Integraciones</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Card variant="outlined" sx={{ p: 1, backgroundColor: '#f8fafc' }}>
                    <Typography variant="caption" color="text.secondary">
                      Configuracion contextual
                    </Typography>
                    {selected.type.startsWith('connect.') && (
                      <Stack spacing={1} sx={{ mt: 0.8 }}>
                        <Box sx={{ p: 1, borderRadius: 1, border: '1px solid #bae6fd', bgcolor: '#f0f9ff' }}>
                          <Typography variant="caption" fontWeight={700}>
                            Integracion de canal
                          </Typography>
                          <Typography variant="caption" display="block" color="text.secondary">
                            Capacidad: {selectedIntegration?.capabilities?.join(', ') || 'send, status'}
                          </Typography>
                          <Typography
                            variant="caption"
                            display="block"
                            color={selectedIntegration?.connected ? 'success.main' : 'warning.main'}
                          >
                            Estado auth: {selectedIntegration?.connected ? 'Conectado' : 'No conectado'}
                          </Typography>
                          <Typography
                            variant="caption"
                            display="block"
                            color={selectedIntegration?.secretsConfigured ? 'success.main' : 'warning.main'}
                          >
                            Secret requerido: {selectedIntegration?.secretsConfigured ? 'Resuelto' : 'Pendiente'}
                          </Typography>
                          <Typography variant="caption" display="block" color="text.secondary">
                            Fuente: {selectedIntegration?.displayName || 'Sin canal configurado'}
                          </Typography>
                        </Box>
                        <TextField
                          label="Destinatario"
                          size="small"
                          value={selected.config?.recipient ?? ''}
                          onChange={(e) => onUpdateActivityConfig(selectedIndex, 'recipient', e.target.value)}
                        />
                        {selected.type === 'connect.send_whatsapp_template' && (
                          <>
                            <TextField
                              label="Plantilla de WhatsApp"
                              select
                              size="small"
                              value={selected.config?.templateId ?? ''}
                              helperText={
                                connectTemplates.length === 0
                                  ? 'No hay plantillas cargadas en Connect.'
                                  : 'Selecciona una plantilla aprobada.'
                              }
                              onChange={(e) => {
                                const template = connectTemplates.find((x) => x.id === e.target.value);
                                onUpdateActivity(selectedIndex, {
                                  config: {
                                    ...(selected.config ?? {}),
                                    templateId: e.target.value,
                                    channel: template?.channel || selected.config?.channel || 'whatsapp',
                                    content: template?.body || selected.config?.content || '',
                                  },
                                });
                              }}
                            >
                              <MenuItem value="">Sin plantilla</MenuItem>
                              {connectTemplates.map((template) => (
                                <MenuItem key={template.id} value={template.id}>
                                  {template.name}
                                </MenuItem>
                              ))}
                            </TextField>
                            {selectedTemplate && (
                              <Box sx={{ p: 1, borderRadius: 1, bgcolor: '#f8fafc', border: '1px solid #e2e8f0' }}>
                                <Typography variant="caption" fontWeight={700}>
                                  Vista previa
                                </Typography>
                                <Typography variant="caption" display="block" color="text.secondary">
                                  {selectedTemplate.body}
                                </Typography>
                                <Stack direction="row" spacing={0.5} flexWrap="wrap" sx={{ mt: 0.8 }}>
                                  {selectedTemplateVariables.length === 0 ? (
                                    <Chip size="small" label="Sin variables" />
                                  ) : (
                                    selectedTemplateVariables.map((variable) => (
                                      <Chip key={variable} size="small" label={`{{${variable}}}`} />
                                    ))
                                  )}
                                </Stack>
                              </Box>
                            )}
                          </>
                        )}
                        <TextField
                          label="Mensaje / Contenido"
                          size="small"
                          value={selected.config?.content ?? ''}
                          onChange={(e) => onUpdateActivityConfig(selectedIndex, 'content', e.target.value)}
                        />
                      </Stack>
                    )}
                    {selected.type.startsWith('human.') && (
                      <Stack spacing={1} sx={{ mt: 0.8 }}>
                        <TextField
                          label="Cola / Equipo"
                          size="small"
                          value={selected.config?.queue ?? ''}
                          onChange={(e) => onUpdateActivityConfig(selectedIndex, 'queue', e.target.value)}
                        />
                        <TextField
                          label="Prioridad"
                          size="small"
                          value={selected.config?.priority ?? ''}
                          onChange={(e) => onUpdateActivityConfig(selectedIndex, 'priority', e.target.value)}
                        />
                      </Stack>
                    )}
                  </Card>
                </AccordionDetails>
              </Accordion>
            )}
            {selected.type === 'ai.agent' && (
              <Card variant="outlined" sx={{ p: 1.2 }}>
                <Tabs value={aiTab} onChange={(_, v) => setAiTab(v)} variant="scrollable">
                  <Tab label="General" />
                  <Tab label="Herramientas" />
                  <Tab label="Contexto" />
                  <Tab label="Avanzado" />
                </Tabs>
                {aiTab === 0 && (
                  <Stack spacing={1} sx={{ mt: 1.2 }}>
                    <TextField
                      label="Agente publicado"
                      select
                      value={selected.config?.agentId ?? selected.aiAgent?.agentId ?? ''}
                      onChange={(e) => selectPublishedAgent(e.target.value)}
                      size="small"
                    >
                      <MenuItem value="">Seleccionar agente</MenuItem>
                      {visibleAgents.map((agent) => (
                        <MenuItem key={agent.id} value={agent.id}>
                          {agent.name}
                          {recommendedAgentIds.has(agent.id) ? ' (recomendado)' : ''}
                        </MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      label="Entrada al agente"
                      multiline
                      minRows={3}
                      value={selected.config?.input ?? selected.aiAgent?.input ?? DEFAULT_AI_AGENT_CONFIG.input}
                      helperText="Usa variables del evento o pasos previos, por ejemplo {{payload.content}}."
                      onChange={(e) => {
                        onUpdateActivityConfig(selectedIndex, 'input', e.target.value);
                        onUpdateAiAgentConfig(selectedIndex, { input: e.target.value });
                      }}
                      size="small"
                    />
                    <TextField
                      label="Contexto del flujo"
                      multiline
                      minRows={2}
                      value={selected.config?.context ?? selected.aiAgent?.context ?? ''}
                      helperText="Contexto adicional del canal, cliente o intencion detectada."
                      onChange={(e) => {
                        onUpdateActivityConfig(selectedIndex, 'context', e.target.value);
                        onUpdateAiAgentConfig(selectedIndex, { context: e.target.value });
                      }}
                      size="small"
                    />
                    <Alert severity="info">
                      Este nodo no redefine el agente. Ejecuta el agente seleccionado con el contexto del workflow.
                    </Alert>
                    {selectedAgent && (
                      <Box sx={{ p: 1, borderRadius: 1, border: '1px dashed #93c5fd', bgcolor: '#f8fbff' }}>
                        <Typography variant="caption" fontWeight={800}>
                          Preview del subflujo
                        </Typography>
                        <Stack direction="row" spacing={0.8} flexWrap="wrap" sx={{ mt: 0.8 }}>
                          <Chip size="small" label={`${selectedAgent.stepsCount ?? 0} pasos internos`} />
                          <Chip size="small" label={`${selectedAgent.toolsCount ?? 0} tools autorizadas`} />
                          {selectedAgent.provider && <Chip size="small" label={selectedAgent.provider} />}
                          {selectedAgent.primaryModel && <Chip size="small" label={selectedAgent.primaryModel} />}
                        </Stack>
                        <Typography variant="caption" display="block" color="text.secondary" sx={{ mt: 0.8 }}>
                          El detalle del subflujo se edita en Agent Studio y se ejecuta como una unidad dentro de este workflow.
                        </Typography>
                      </Box>
                    )}
                  </Stack>
                )}
                {aiTab === 1 && (
                  <Stack spacing={1} sx={{ mt: 1.2 }}>
                    <Alert severity="info">
                      Las herramientas autorizadas se administran en Agent Studio para evitar duplicar seguridad y permisos.
                    </Alert>
                    <Box sx={{ p: 1, borderRadius: 1, border: '1px solid #e2e8f0', bgcolor: '#f8fafc' }}>
                      <Typography variant="caption" fontWeight={700}>
                        Inventario disponible
                      </Typography>
                      <Typography variant="caption" display="block" color="text.secondary">
                        Tools registradas en plataforma: {availableTools.length}
                      </Typography>
                      <Typography variant="caption" display="block" color="text.secondary">
                        Modelos registrados en plataforma: {availableModels.length}
                      </Typography>
                    </Box>
                    <Button variant="outlined" href={selectedAgent ? paths.dashboard.agentEdit(selectedAgent.id) : paths.dashboard.agents}>
                      Abrir Agent Studio
                    </Button>
                  </Stack>
                )}
                {aiTab === 2 && (
                  <Stack spacing={1} sx={{ mt: 1.2 }}>
                    <TextField
                      label="Variables esperadas"
                      multiline
                      minRows={3}
                      value={selected.config?.input ?? selected.aiAgent?.input ?? DEFAULT_AI_AGENT_CONFIG.input}
                      helperText="Define que texto o JSON recibira el agente desde este workflow."
                      onChange={(e) => {
                        onUpdateActivityConfig(selectedIndex, 'input', e.target.value);
                        onUpdateAiAgentConfig(selectedIndex, { input: e.target.value });
                      }}
                      size="small"
                    />
                    <Typography variant="caption" color="text.secondary">
                      El conocimiento documental y la memoria pertenecen al agente publicado. Aqui solo pasamos contexto runtime.
                    </Typography>
                  </Stack>
                )}
                {aiTab === 3 && (
                  <Stack spacing={1} sx={{ mt: 1.2 }}>
                    <TextField
                      label="Latencia maxima (ms)"
                      type="number"
                      value={selected.aiAgent?.maxLatencyMs ?? DEFAULT_AI_AGENT_CONFIG.maxLatencyMs}
                      onChange={(e) =>
                        onUpdateAiAgentConfig(selectedIndex, {
                          maxLatencyMs: Number(e.target.value || DEFAULT_AI_AGENT_CONFIG.maxLatencyMs),
                        })
                      }
                      size="small"
                    />
                    <TextField
                      label="Costo maximo (USD)"
                      type="number"
                      value={selected.aiAgent?.maxCostUsd ?? DEFAULT_AI_AGENT_CONFIG.maxCostUsd}
                      onChange={(e) =>
                        onUpdateAiAgentConfig(selectedIndex, {
                          maxCostUsd: Number(e.target.value || DEFAULT_AI_AGENT_CONFIG.maxCostUsd),
                        })
                      }
                      size="small"
                    />
                    <FormControlLabel
                      control={
                        <Checkbox
                          checked={selected.aiAgent?.dlpEnabled ?? DEFAULT_AI_AGENT_CONFIG.dlpEnabled}
                          onChange={(e) => onUpdateAiAgentConfig(selectedIndex, { dlpEnabled: e.target.checked })}
                        />
                      }
                      label="Habilitar DLP"
                    />
                    <Button variant="outlined" onClick={() => onOpenAiConfig(selectedIndex)}>
                      Abrir configuracion tecnica heredada
                    </Button>
                    <Button variant="text" onClick={() => setShowAdvanced((value) => !value)}>
                      {showAdvanced ? 'Ocultar configuracion tecnica' : 'Mostrar configuracion tecnica'}
                    </Button>
                  </Stack>
                )}
              </Card>
            )}
            {(selected.type !== 'ai.agent' || showAdvanced) && (
            <Accordion expanded={inspectorSection === 'runtime'} onChange={(_, e) => setInspectorSection(e ? 'runtime' : '')}>
              <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" />}>
                <Typography variant="subtitle2">Runtime</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <Stack spacing={1} sx={{ mb: 1 }}>
                  <TextField
                    label="ID interno"
                    value={selected.id}
                    onChange={(e) => onUpdateActivity(selectedIndex, { id: e.target.value })}
                    size="small"
                  />
                  <TextField
                    label="Siguiente nodo"
                    value={selected.next ?? ''}
                    onChange={(e) => onUpdateActivity(selectedIndex, { next: e.target.value || undefined })}
                    size="small"
                  />
                  <TextField
                    label="Exito"
                    value={selected.onSuccess ?? ''}
                    onChange={(e) => onUpdateActivity(selectedIndex, { onSuccess: e.target.value || undefined })}
                    size="small"
                  />
                  <TextField
                    label="Fallo"
                    value={selected.onFailure ?? ''}
                    onChange={(e) => onUpdateActivity(selectedIndex, { onFailure: e.target.value || undefined })}
                    size="small"
                  />
                </Stack>
                <Button
                  size="small"
                  variant="text"
                  onClick={() => setShowAdvanced((s) => !s)}
                  sx={{ justifyContent: 'flex-start', mb: 1 }}
                >
                  {showAdvanced ? 'Ocultar avanzado' : 'Mostrar avanzado'}
                </Button>
                <Collapse in={showAdvanced}>
                  <Stack spacing={1}>
                    <TextField
                      label="Timeout (ms)"
                      size="small"
                      type="number"
                      value={selected.timeoutMs ?? 30000}
                      onChange={(e) => onUpdateActivity(selectedIndex, { timeoutMs: Number(e.target.value || 30000) })}
                    />
                    <TextField
                      label="Reintentos"
                      size="small"
                      type="number"
                      value={selected.retryCount ?? 0}
                      onChange={(e) => onUpdateActivity(selectedIndex, { retryCount: Number(e.target.value || 0) })}
                    />
                    <TextField
                      label="Delay reintento (ms)"
                      size="small"
                      type="number"
                      value={selected.retryDelayMs ?? 0}
                      onChange={(e) => onUpdateActivity(selectedIndex, { retryDelayMs: Number(e.target.value || 0) })}
                    />
                  </Stack>
                </Collapse>
              </AccordionDetails>
            </Accordion>
            )}

            {(selected.type !== 'ai.agent' || showAdvanced) && (
            <Accordion expanded={inspectorSection === 'debug'} onChange={(_, e) => setInspectorSection(e ? 'debug' : '')}>
              <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" />}>
                <Typography variant="subtitle2">Debug</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
                  <Typography variant="caption" color="text.secondary">
                    Config
                    {requiredKeys.length
                      ? ` (requeridos: ${requiredKeys.join(', ')})`
                      : ''}
                  </Typography>
                  <Button size="small" onClick={() => onAddActivityConfig(selectedIndex)}>
                    Add
                  </Button>
                </Stack>
                {Object.entries(selected.config ?? {}).map(([key, value]) => (
              <Grid container spacing={1} key={`${selected.id}_${key}`}>
                <Grid item xs={4}>
                  <TextField
                    size="small"
                    label="Key"
                    value={key}
                    onChange={(e) => {
                      const nextKey = e.target.value.trim();
                      if (!nextKey || nextKey === key) return;
                      const cfg = { ...(selected.config ?? {}) };
                      const currentValue = cfg[key] ?? '';
                      delete cfg[key];
                      cfg[nextKey] = currentValue;
                      onUpdateActivity(selectedIndex, { config: cfg });
                    }}
                    fullWidth
                  />
                </Grid>
                <Grid item xs={7}>
                  <TextField
                    size="small"
                    label="Value"
                    value={value}
                    onChange={(e) => onUpdateActivityConfig(selectedIndex, key, e.target.value)}
                    fullWidth
                  />
                </Grid>
                <Grid item xs={1}>
                  <Button color="error" onClick={() => onRemoveActivityConfig(selectedIndex, key)} fullWidth>
                    X
                  </Button>
                </Grid>
              </Grid>
                ))}
              </AccordionDetails>
            </Accordion>
            )}
            <Button color="error" variant="outlined" onClick={() => onRemoveActivity(selectedIndex)}>
              Eliminar nodo
            </Button>
          </Stack>
        )}
      </Drawer>

      <Drawer
        anchor="right"
        open={showValidation}
        onClose={() => setShowValidation(false)}
        PaperProps={{ sx: { width: 360, p: 2 } }}
      >
        <Typography variant="subtitle1" sx={{ mb: 1 }}>Validaciones</Typography>
        {validationErrors.length === 0 ? (
          <Alert severity="success">Sin errores de validacion.</Alert>
        ) : (
          <Stack spacing={1}>
            {validationErrors.map((err, idx) => (
              <Alert key={`v_${idx}`} severity="warning">{err}</Alert>
            ))}
          </Stack>
        )}
      </Drawer>
    </Card>
  );
}


