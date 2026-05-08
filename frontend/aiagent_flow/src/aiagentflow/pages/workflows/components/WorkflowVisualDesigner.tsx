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
import MenuItem from '@mui/material/MenuItem';
import Checkbox from '@mui/material/Checkbox';
import TextField from '@mui/material/TextField';
import FormGroup from '@mui/material/FormGroup';
import Typography from '@mui/material/Typography';
import FormControlLabel from '@mui/material/FormControlLabel';

import { Iconify } from 'src/components/iconify';

import { DEFAULT_AI_AGENT_CONFIG } from '../constants';

import type { ToolOption, ModelOption, WorkflowActivityNode } from '../types';

type Props = {
  activities: WorkflowActivityNode[];
  allowedTypes: string[];
  requiredConfigByType: Record<string, string[]>;
  validationErrors: string[];
  availableModels: ModelOption[];
  availableTools: ToolOption[];
  onAddActivity: () => void;
  onUpdateActivity: (index: number, patch: Partial<WorkflowActivityNode>) => void;
  onRemoveActivity: (index: number) => void;
  onApplyTypePreset: (index: number, activityType: string) => void;
  onOpenAiConfig: (index: number) => void;
  onUpdateAiAgentConfig: (index: number, patch: Partial<typeof DEFAULT_AI_AGENT_CONFIG>) => void;
  onAddActivityConfig: (index: number) => void;
  onUpdateActivityConfig: (index: number, key: string, value: string) => void;
  onRemoveActivityConfig: (index: number, key: string) => void;
};

const nodeColorByType = (type: string) => {
  if (type === 'ai.agent') return '#2667ff';
  if (type.startsWith('connect.')) return '#0ea5a3';
  if (type.startsWith('kyc.')) return '#f59e0b';
  if (type.startsWith('payments.')) return '#7c3aed';
  return '#64748b';
};

function WorkflowNodeCard({ data, selected }: NodeProps) {
  const activityType = String(data.activityType ?? '');
  const label = String(data.label ?? activityType);
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
            {category}
          </Typography>
          <Stack direction="row" spacing={0.4}>
            <Button size="small" onClick={() => data.onDuplicate?.()} sx={{ minWidth: 24, px: 0.6 }}>
              D
            </Button>
            <Button size="small" color="error" onClick={() => data.onDelete?.()} sx={{ minWidth: 24, px: 0.6 }}>
              X
            </Button>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ wordBreak: 'break-all' }}>
          {activityType}
        </Typography>
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

function AiWorkflowNode({ data, selected }: NodeProps) {
  const label = String(data.label ?? 'AI Agent');
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
            <Button size="small" onClick={() => data.onDuplicate?.()} sx={{ minWidth: 24, px: 0.6 }}>
              D
            </Button>
            <Button size="small" color="error" onClick={() => data.onDelete?.()} sx={{ minWidth: 24, px: 0.6 }}>
              X
            </Button>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          model orchestration
        </Typography>
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

function ConnectWorkflowNode({ data, selected }: NodeProps) {
  const label = String(data.label ?? 'Connect');
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
            <Button size="small" onClick={() => data.onDuplicate?.()} sx={{ minWidth: 24, px: 0.6 }}>
              D
            </Button>
            <Button size="small" color="error" onClick={() => data.onDelete?.()} sx={{ minWidth: 24, px: 0.6 }}>
              X
            </Button>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          outbound and status
        </Typography>
      </Stack>
      <Handle type="source" position={Position.Right} id="next" style={{ top: '35%' }} />
      <Handle type="source" position={Position.Right} id="success" style={{ top: '55%', background: '#16a34a' }} />
      <Handle type="source" position={Position.Right} id="failure" style={{ top: '75%', background: '#dc2626' }} />
    </Box>
  );
}

function HumanWorkflowNode({ data, selected }: NodeProps) {
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
            <Button size="small" onClick={() => data.onDuplicate?.()} sx={{ minWidth: 24, px: 0.6 }}>
              D
            </Button>
            <Button size="small" color="error" onClick={() => data.onDelete?.()} sx={{ minWidth: 24, px: 0.6 }}>
              X
            </Button>
          </Stack>
        </Stack>
        <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          {label}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          handoff and assignment
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
  onDuplicate: (index: number) => void,
  onDelete: (index: number) => void
): Node[] =>
  activities.map((activity, idx) => ({
    id: activity.id || `step-${idx + 1}`,
    position: activity.position ?? { x: 120 + (idx % 3) * 280, y: 100 + Math.floor(idx / 3) * 180 },
    data: {
      label: activity.name || activity.id || activity.type,
      activityType: activity.type,
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
  }));

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
  availableModels,
  availableTools,
  onAddActivity,
  onUpdateActivity,
  onRemoveActivity,
  onApplyTypePreset,
  onOpenAiConfig,
  onUpdateAiAgentConfig,
  onAddActivityConfig,
  onUpdateActivityConfig,
  onRemoveActivityConfig,
}: Props) {
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [search, setSearch] = useState('');
  const [aiTab, setAiTab] = useState(0);
  const [nodes, setNodes, onNodesChange] = useNodesState([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState([]);

  const duplicateNodeAt = useCallback((index: number) => {
    const source = activities[index];
    if (!source) return;
    onAddActivity();
    const nextIndex = activities.length;
    const newId = `${source.id || 'step'}_copy_${Date.now()}`;
    onUpdateActivity(nextIndex, {
      ...source,
      id: newId,
      name: source.name ? `${source.name} (copy)` : undefined,
      position: source.position ? { x: source.position.x + 40, y: source.position.y + 40 } : undefined,
    });
  }, [activities, onAddActivity, onUpdateActivity]);

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
    setNodes(toNodes(activities, duplicateNodeAt, deleteNodeAt));
    setEdges(toEdges(activities));
  }, [activities, deleteNodeAt, duplicateNodeAt, setEdges, setNodes]);

  const selected = useMemo(
    () => (selectedIndex !== null && selectedIndex >= 0 ? activities[selectedIndex] : null),
    [activities, selectedIndex]
  );

  const categorizedTypes = useMemo(() => {
    const q = search.trim().toLowerCase();
    const filtered = allowedTypes.filter((t) => t.toLowerCase().includes(q));
    return {
      ai: filtered.filter((t) => t.startsWith('ai.')),
      connect: filtered.filter((t) => t.startsWith('connect.')),
      kyc: filtered.filter((t) => t.startsWith('kyc.')),
      payments: filtered.filter((t) => t.startsWith('payments.')),
      other: filtered.filter(
        (t) =>
          !t.startsWith('ai.') &&
          !t.startsWith('connect.') &&
          !t.startsWith('kyc.') &&
          !t.startsWith('payments.')
      ),
    };
  }, [allowedTypes, search]);

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
    onAddActivity();
    const index = activities.length;
    onUpdateActivity(index, {
      id: `step-${Date.now()}`,
      type,
      aiAgent: type === 'ai.agent' ? { ...DEFAULT_AI_AGENT_CONFIG } : undefined,
    });
    onApplyTypePreset(index, type);
  };

  return (
    <Card variant="outlined" sx={{ p: 1.5 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
        <Typography variant="subtitle1">Workflow Canvas</Typography>
        <Stack direction="row" spacing={1}>
          <Button size="small" variant="outlined" onClick={applyAutoLayoutGraph}>
            Auto Layout Graph
          </Button>
          <Button size="small" variant="outlined" onClick={applyAutoLayoutGrid}>
            Auto Layout
          </Button>
          <Button size="small" onClick={onAddActivity} startIcon={<Iconify icon="mingcute:add-line" />}>
            Add Step
          </Button>
        </Stack>
      </Stack>

      {validationErrors.length > 0 && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {validationErrors.slice(0, 3).join(' | ')}
        </Alert>
      )}

      <Grid container spacing={1.5}>
        <Grid item xs={12} md={3}>
          <Card variant="outlined" sx={{ p: 1.2, height: 560, overflow: 'auto' }}>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Activity Palette
            </Typography>
            <TextField
              size="small"
              label="Search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              fullWidth
              sx={{ mb: 1 }}
            />
            <Stack spacing={1}>
              {Object.entries(categorizedTypes).map(([category, types]) =>
                types.length > 0 ? (
                  <Box key={category} sx={{ mb: 0.5 }}>
                    <Stack direction="row" alignItems="center" spacing={0.8} sx={{ mb: 0.6 }}>
                      <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'uppercase' }}>
                        {category}
                      </Typography>
                      <Chip size="small" label={types.length} />
                    </Stack>
                    <Stack spacing={0.6}>
                      {types.map((type) => (
                        <Button
                          key={type}
                          variant="outlined"
                          onClick={() => addByType(type)}
                          sx={{ justifyContent: 'flex-start', textTransform: 'none' }}
                        >
                          {type}
                        </Button>
                      ))}
                    </Stack>
                  </Box>
                ) : null
              )}
            </Stack>
          </Card>
        </Grid>
        <Grid item xs={12} md={9}>
          <Box sx={{ height: 560, border: '1px solid', borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodeTypes={{
                workflowNode: WorkflowNodeCard,
                aiNode: AiWorkflowNode,
                connectNode: ConnectWorkflowNode,
                humanNode: HumanWorkflowNode,
              }}
              onNodesChange={onNodesChange}
              onEdgesChange={onEdgesChange}
              onConnect={onConnect}
              onNodeDragStop={(_, node) => {
                const index = Number(node.data.index);
                if (Number.isNaN(index) || index < 0) return;
                onUpdateActivity(index, { position: node.position });
              }}
              onNodeClick={(_, node) => setSelectedIndex(Number(node.data.index))}
              fitView
            >
              <MiniMap />
              <Controls />
              <Background />
            </ReactFlow>
          </Box>
        </Grid>
      </Grid>

      <Drawer
        anchor="right"
        open={selected !== null}
        onClose={() => setSelectedIndex(null)}
        PaperProps={{ sx: { width: 420, p: 2 } }}
      >
        {selected && selectedIndex !== null && (
          <Stack spacing={1}>
            <Typography variant="subtitle1">Node Config</Typography>
            <Divider />
            <TextField
              label="ID"
              value={selected.id}
              onChange={(e) => onUpdateActivity(selectedIndex, { id: e.target.value })}
              size="small"
            />
            <TextField
              label="Type"
              select
              value={selected.type}
              onChange={(e) => onUpdateActivity(selectedIndex, { type: e.target.value })}
              size="small"
            >
              {allowedTypes.map((type) => (
                <MenuItem key={type} value={type}>
                  {type}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Name"
              value={selected.name ?? ''}
              onChange={(e) => onUpdateActivity(selectedIndex, { name: e.target.value || undefined })}
              size="small"
            />
            <TextField
              label="Next"
              value={selected.next ?? ''}
              onChange={(e) => onUpdateActivity(selectedIndex, { next: e.target.value || undefined })}
              size="small"
            />
            <TextField
              label="On Success"
              value={selected.onSuccess ?? ''}
              onChange={(e) => onUpdateActivity(selectedIndex, { onSuccess: e.target.value || undefined })}
              size="small"
            />
            <TextField
              label="On Failure"
              value={selected.onFailure ?? ''}
              onChange={(e) => onUpdateActivity(selectedIndex, { onFailure: e.target.value || undefined })}
              size="small"
            />
            {(selected.type.startsWith('connect.') || selected.type.startsWith('human.')) && (
              <Card variant="outlined" sx={{ p: 1, backgroundColor: '#f8fafc' }}>
                <Typography variant="caption" color="text.secondary">
                  Contextual Config
                </Typography>
                {selected.type.startsWith('connect.') && (
                  <Stack spacing={1} sx={{ mt: 0.8 }}>
                    <TextField
                      label="Recipient"
                      size="small"
                      value={selected.config?.recipient ?? ''}
                      onChange={(e) => onUpdateActivityConfig(selectedIndex, 'recipient', e.target.value)}
                    />
                    <TextField
                      label="Message / Content"
                      size="small"
                      value={selected.config?.content ?? ''}
                      onChange={(e) => onUpdateActivityConfig(selectedIndex, 'content', e.target.value)}
                    />
                  </Stack>
                )}
                {selected.type.startsWith('human.') && (
                  <Stack spacing={1} sx={{ mt: 0.8 }}>
                    <TextField
                      label="Queue / Team"
                      size="small"
                      value={selected.config?.queue ?? ''}
                      onChange={(e) => onUpdateActivityConfig(selectedIndex, 'queue', e.target.value)}
                    />
                    <TextField
                      label="Priority"
                      size="small"
                      value={selected.config?.priority ?? ''}
                      onChange={(e) => onUpdateActivityConfig(selectedIndex, 'priority', e.target.value)}
                    />
                  </Stack>
                )}
              </Card>
            )}

            {selected.type === 'ai.agent' && (
              <>
                <Tabs value={aiTab} onChange={(_, v) => setAiTab(v)}>
                  <Tab label="General" />
                  <Tab label="Tools" />
                  <Tab label="Knowledge" />
                  <Tab label="Advanced" />
                </Tabs>
                {aiTab === 0 && (
                  <Stack spacing={1}>
                    <TextField
                      label="Model"
                      select
                      value={selected.aiAgent?.model ?? DEFAULT_AI_AGENT_CONFIG.model}
                      onChange={(e) => onUpdateAiAgentConfig(selectedIndex, { model: e.target.value })}
                      size="small"
                    >
                      {(availableModels.length > 0
                        ? availableModels.map((m) => m.modelId)
                        : ['gpt-4o', 'gpt-4o-mini']
                      ).map((m) => (
                        <MenuItem key={m} value={m}>
                          {m}
                        </MenuItem>
                      ))}
                    </TextField>
                    <TextField
                      label="Instructions"
                      multiline
                      minRows={4}
                      value={selected.aiAgent?.instructions ?? ''}
                      onChange={(e) => onUpdateAiAgentConfig(selectedIndex, { instructions: e.target.value })}
                      size="small"
                    />
                  </Stack>
                )}
                {aiTab === 1 && (
                  <FormGroup>
                    {(availableTools.length > 0
                      ? availableTools
                      : [{ key: 'http.request', displayName: 'HTTP Request' }]
                    ).map((tool) => {
                      const selectedToolsSet = new Set(selected.aiAgent?.tools ?? []);
                      const checked = selectedToolsSet.has(tool.key);
                      return (
                        <FormControlLabel
                          key={tool.key}
                          control={
                            <Checkbox
                              checked={checked}
                              onChange={(e) => {
                                const next = new Set(selected.aiAgent?.tools ?? []);
                                if (e.target.checked) next.add(tool.key);
                                else next.delete(tool.key);
                                onUpdateAiAgentConfig(selectedIndex, { tools: Array.from(next) });
                              }}
                            />
                          }
                          label={tool.displayName || tool.key}
                        />
                      );
                    })}
                  </FormGroup>
                )}
                {aiTab === 2 && (
                  <Stack spacing={1}>
                    <TextField
                      label="Knowledge Sources"
                      multiline
                      minRows={3}
                      value={(selected.aiAgent?.knowledge ?? []).join(',')}
                      onChange={(e) =>
                        onUpdateAiAgentConfig(selectedIndex, {
                          knowledge: e.target.value
                            .split(',')
                            .map((x) => x.trim())
                            .filter(Boolean),
                        })
                      }
                      size="small"
                    />
                    <TextField
                      label="Context"
                      multiline
                      minRows={3}
                      value={selected.aiAgent?.context ?? ''}
                      onChange={(e) => onUpdateAiAgentConfig(selectedIndex, { context: e.target.value })}
                      size="small"
                    />
                  </Stack>
                )}
                {aiTab === 3 && (
                  <Stack spacing={1}>
                    <TextField
                      label="Fallback Model"
                      value={selected.aiAgent?.fallbackModel ?? DEFAULT_AI_AGENT_CONFIG.fallbackModel}
                      onChange={(e) => onUpdateAiAgentConfig(selectedIndex, { fallbackModel: e.target.value })}
                      size="small"
                    />
                    <TextField
                      label="Max Latency (ms)"
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
                      label="Max Cost (USD)"
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
                      label="Enable DLP"
                    />
                    <Button variant="outlined" onClick={() => onOpenAiConfig(selectedIndex)}>
                      Advanced Dialog
                    </Button>
                  </Stack>
                )}
              </>
            )}

            <Divider />
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Typography variant="caption" color="text.secondary">
                Config
                {requiredConfigByType[selected.type]?.length
                  ? ` (required: ${requiredConfigByType[selected.type].join(', ')})`
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
            <Button color="error" variant="outlined" onClick={() => onRemoveActivity(selectedIndex)}>
              Remove Node
            </Button>
          </Stack>
        )}
      </Drawer>
    </Card>
  );
}
