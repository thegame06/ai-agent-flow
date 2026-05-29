import '@xyflow/react/dist/style.css';

import type { AppDispatch } from 'src/aiagentflow/store';
import type { Edge, Node, NodeProps, Connection, EdgeChange } from '@xyflow/react';

import { useDispatch } from 'react-redux';
import { useMemo, useState, useEffect, useCallback } from 'react';
import {
  Handle,
  addEdge,
  MiniMap,
  Controls,
  Position,
  ReactFlow,
  Background,
  MarkerType,
  useEdgesState,
  useNodesState,
} from '@xyflow/react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { alpha, useTheme } from '@mui/material/styles';

import { Iconify } from 'src/components/iconify';

import { addStep, removeStep, updateStep } from './designerSlice';

import type { AgentStep } from './types';

type AgentStepType = AgentStep['type'];

type StepMeta = {
  value: AgentStepType;
  label: string;
  description: string;
  icon: string;
  color: string;
};

type AgentNodeData = {
  index: number;
  label: string;
  type: AgentStepType;
  description: string;
};

type AgentDesignerNode = Node<AgentNodeData>;

const STEP_TYPES: StepMeta[] = [
  {
    value: 'think',
    label: 'Razonar',
    description: 'Interpreta la entrada y prepara el siguiente paso.',
    icon: 'mdi:head-lightbulb',
    color: '#06b6d4',
  },
  {
    value: 'plan',
    label: 'Planificar',
    description: 'Divide una tarea compleja en acciones concretas.',
    icon: 'mdi:map-outline',
    color: '#6366f1',
  },
  {
    value: 'act',
    label: 'Actuar',
    description: 'Ejecuta una accion interna del agente.',
    icon: 'mdi:lightning-bolt',
    color: '#f59e0b',
  },
  {
    value: 'tool_call',
    label: 'Usar herramienta',
    description: 'Llama una herramienta autorizada en la configuracion del asistente.',
    icon: 'mdi:wrench-outline',
    color: '#475569',
  },
  {
    value: 'observe',
    label: 'Observar',
    description: 'Evalua el resultado de una accion o tool.',
    icon: 'mdi:eye-outline',
    color: '#22c55e',
  },
  {
    value: 'decide',
    label: 'Decision',
    description: 'Elige la siguiente ruta con una regla simple.',
    icon: 'mdi:source-branch',
    color: '#ec4899',
  },
  {
    value: 'aggregate',
    label: 'Agregador',
    description: 'Combina resultados de pasos anteriores.',
    icon: 'mdi:graph-outline',
    color: '#3b82f6',
  },
  {
    value: 'human_review',
    label: 'Revision humana',
    description: 'Pausa para validacion humana cuando aplique.',
    icon: 'mdi:account-check',
    color: '#92400e',
  },
];

function stepMeta(type: AgentStepType): StepMeta {
  return STEP_TYPES.find((item) => item.value === type) ?? STEP_TYPES[0];
}

function defaultConfigForStepType(type: AgentStepType): Record<string, unknown> {
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

function AgentStepNode({ data }: NodeProps<AgentDesignerNode>) {
  const meta = stepMeta(data.type);

  return (
    <Card
      variant="outlined"
      sx={{
        width: 236,
        borderRadius: 2,
        borderColor: alpha(meta.color, 0.5),
        boxShadow: '0 10px 28px rgba(15,23,42,0.08)',
        overflow: 'visible',
      }}
    >
      <Handle type="target" position={Position.Left} style={{ background: meta.color }} />
      <Box sx={{ px: 1.2, py: 0.8, bgcolor: alpha(meta.color, 0.08), borderBottom: '1px solid', borderColor: alpha(meta.color, 0.18) }}>
        <Stack direction="row" spacing={0.8} alignItems="center">
          <Box sx={{ color: meta.color, display: 'flex' }}>
            <Iconify icon={meta.icon} width={18} />
          </Box>
          <Typography variant="caption" fontWeight={800} noWrap>
            {meta.label}
          </Typography>
          <Chip size="small" label={`#${data.index + 1}`} sx={{ ml: 'auto' }} />
        </Stack>
      </Box>
      <Stack spacing={0.6} sx={{ p: 1.2 }}>
        <Typography variant="subtitle2" noWrap>
          {data.label}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ minHeight: 34 }}>
          {data.description || meta.description}
        </Typography>
      </Stack>
      <Handle type="source" position={Position.Right} style={{ background: meta.color }} />
    </Card>
  );
}

const nodeTypes = {
  agentStep: AgentStepNode,
};

function toNodes(steps: AgentStep[]): AgentDesignerNode[] {
  return steps.map((step, index) => ({
    id: step.id,
    type: 'agentStep',
    position: step.position || { x: 120 + (index % 3) * 300, y: 100 + Math.floor(index / 3) * 180 },
    data: {
      index,
      label: step.label,
      type: step.type,
      description: step.description,
    },
  }));
}

function toEdges(steps: AgentStep[], color: string): Edge[] {
  return steps.flatMap((step) =>
    (step.connections ?? []).map((targetId) => ({
      id: `${step.id}-${targetId}`,
      source: step.id,
      target: targetId,
      type: 'smoothstep',
      animated: true,
      markerEnd: {
        type: MarkerType.ArrowClosed,
        color,
      },
      style: {
        stroke: color,
        strokeWidth: 2,
      },
    }))
  );
}

interface AgentFlowCanvasProps {
  steps: AgentStep[];
  agentName?: string;
}

export default function AgentFlowCanvas({ steps, agentName }: AgentFlowCanvasProps) {
  const theme = useTheme();
  const dispatch = useDispatch<AppDispatch>();
  const [selectedId, setSelectedId] = useState<string | null>(steps[0]?.id ?? null);
  const [showTechnicalConfig, setShowTechnicalConfig] = useState(false);
  const [nodes, setNodes, onNodesChange] = useNodesState<AgentDesignerNode>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);

  const selectedStep = useMemo(
    () => (selectedId ? steps.find((step) => step.id === selectedId) ?? null : null),
    [selectedId, steps]
  );

  useEffect(() => {
    setNodes(toNodes(steps));
    setEdges(toEdges(steps, theme.palette.primary.main));
  }, [setEdges, setNodes, steps, theme.palette.primary.main]);

  const createStep = (type: AgentStepType) => {
    const meta = stepMeta(type);
    const id = `agent-step-${Date.now()}`;
    const previous = steps.at(-1);
    const newStep: AgentStep = {
      id,
      type,
      label: meta.label,
      description: meta.description,
      config: defaultConfigForStepType(type),
      position: { x: 120 + (steps.length % 3) * 300, y: 100 + Math.floor(steps.length / 3) * 180 },
      connections: [],
    };

    dispatch(addStep(newStep));

    if (previous) {
      dispatch(
        updateStep({
          id: previous.id,
          changes: { connections: Array.from(new Set([...(previous.connections ?? []), id])) },
        })
      );
    }

    setSelectedId(id);
  };

  const onConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target) return;

      setEdges((current) =>
        addEdge(
          {
            ...connection,
            type: 'smoothstep',
            animated: true,
            markerEnd: { type: MarkerType.ArrowClosed, color: theme.palette.primary.main },
          },
          current
        )
      );

      const sourceStep = steps.find((step) => step.id === connection.source);
      if (!sourceStep) return;

      dispatch(
        updateStep({
          id: connection.source,
          changes: {
            connections: Array.from(new Set([...(sourceStep.connections ?? []), connection.target])),
          },
        })
      );
    },
    [dispatch, setEdges, steps, theme.palette.primary.main]
  );

  const handleEdgesChange = (changes: EdgeChange<Edge>[]) => {
    onEdgesChange(changes);
    changes
      .filter((change) => change.type === 'remove')
      .forEach((change) => {
        const [source, target] = change.id.split('-');
        const sourceStep = steps.find((step) => step.id === source);
        if (!sourceStep || !target) return;
        dispatch(
          updateStep({
            id: source,
            changes: { connections: (sourceStep.connections ?? []).filter((id) => id !== target) },
          })
        );
      });
  };

  const applyAutoLayout = () => {
    steps.forEach((step, index) => {
      dispatch(
        updateStep({
          id: step.id,
          changes: { position: { x: 120 + (index % 3) * 300, y: 100 + Math.floor(index / 3) * 180 } },
        })
      );
    });
  };

  const updateSelectedConfig = (key: string, value: unknown) => {
    if (!selectedStep) return;
    dispatch(
      updateStep({
        id: selectedStep.id,
        changes: { config: { ...(selectedStep.config ?? {}), [key]: value } },
      })
    );
  };

  const selectedConfig = selectedStep?.config ?? {};

  return (
    <Card variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ px: 2, py: 1.3 }}>
        <Box>
          <Typography variant="subtitle1">Subflujo del agente</Typography>
          <Typography variant="caption" color="text.secondary">
            {agentName || 'Agente'} se ejecuta como subflujo cuando un nodo del la automatizacion lo invoca.
          </Typography>
        </Box>
        <Stack direction="row" spacing={0.8} sx={{ ml: 'auto', mr: 1 }} flexWrap="wrap">
          <Chip size="small" label={`${steps.length} pasos`} />
          <Chip size="small" color={steps.length > 0 ? 'success' : 'warning'} label={steps.length > 0 ? 'Listo' : 'Sin pasos'} />
          <Chip size="small" label="Subflujo reutilizable" />
        </Stack>
        <Stack direction="row" spacing={1}>
          <Button size="small" variant="outlined" onClick={applyAutoLayout}>
            Organizar
          </Button>
          <Button size="small" variant="contained" onClick={() => createStep('think')}>
            Paso basico
          </Button>
        </Stack>
      </Stack>
      <Divider />

      <Box
        sx={{
          position: 'relative',
          height: 640,
          bgcolor: theme.palette.mode === 'dark' ? alpha(theme.palette.background.paper, 0.7) : '#fff',
        }}
      >
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onNodesChange={onNodesChange}
          onEdgesChange={handleEdgesChange}
          onConnect={onConnect}
          onNodeClick={(_, node) => setSelectedId(node.id)}
          onNodeDragStop={(_, node) => {
            dispatch(updateStep({ id: node.id, changes: { position: node.position } }));
          }}
          fitView
          snapToGrid
          snapGrid={[15, 15]}
        >
          <Background
            gap={16}
            size={1}
            color={theme.palette.mode === 'dark' ? alpha(theme.palette.common.white, 0.12) : '#dbeafe'}
          />
          <Controls />
          <MiniMap
            nodeColor={(node) => stepMeta((node.data as AgentNodeData).type).color}
            maskColor={theme.palette.mode === 'dark' ? alpha(theme.palette.common.black, 0.48) : 'rgba(255,255,255,0.72)'}
          />
        </ReactFlow>

        {steps.length === 0 && (
          <Card
            variant="outlined"
            sx={{
              position: 'absolute',
              left: '50%',
              top: '50%',
              width: 360,
              p: 2,
              textAlign: 'center',
              transform: 'translate(-50%, -50%)',
              bgcolor:
                theme.palette.mode === 'dark'
                  ? alpha(theme.palette.background.paper, 0.94)
                  : 'rgba(255,255,255,0.96)',
              boxShadow: '0 16px 42px rgba(15,23,42,0.12)',
            }}
          >
            <Stack spacing={1.2} alignItems="center">
              <Iconify icon="mdi:brain" width={32} color={theme.palette.primary.main} />
              <Box>
                <Typography variant="subtitle1">Define como trabaja el agente</Typography>
                <Typography variant="body2" color="text.secondary">
                  Este subflujo describe como piensa, usa herramientas y valida resultados cuando la automatizacion lo llama.
                </Typography>
              </Box>
              <Stack direction="row" spacing={1}>
                <Button size="small" variant="contained" onClick={() => createStep('think')}>
                  Razonar
                </Button>
                <Button size="small" variant="outlined" onClick={() => createStep('tool_call')}>
                  Usar herramienta
                </Button>
                <Button size="small" variant="outlined" onClick={() => createStep('observe')}>
                  Observar
                </Button>
              </Stack>
            </Stack>
          </Card>
        )}

        <Card
          variant="outlined"
          sx={{
            position: 'absolute',
            left: 24,
            top: 24,
            width: 220,
            maxHeight: 500,
            overflow: 'auto',
            p: 1,
            bgcolor:
              theme.palette.mode === 'dark'
                ? alpha(theme.palette.background.paper, 0.9)
                : 'rgba(255,255,255,0.94)',
            backdropFilter: 'blur(8px)',
          }}
        >
          <Typography variant="caption" color="text.secondary" sx={{ px: 0.5 }}>
            Nodos del agente
          </Typography>
          <Stack spacing={0.7} sx={{ mt: 1 }}>
            {STEP_TYPES.map((type) => (
              <Button
                key={type.value}
                fullWidth
                size="small"
                variant="outlined"
                onClick={() => createStep(type.value)}
                startIcon={<Iconify icon={type.icon} />}
                sx={{
                  justifyContent: 'flex-start',
                  color: type.color,
                  borderColor: alpha(type.color, 0.35),
                  '&:hover': { borderColor: type.color, bgcolor: alpha(type.color, 0.08) },
                }}
              >
                {type.label}
              </Button>
            ))}
          </Stack>
        </Card>

        <Card
          variant="outlined"
          sx={{
            position: 'absolute',
            left: '50%',
            bottom: 18,
            transform: 'translateX(-50%)',
            px: 1,
            py: 0.8,
            borderRadius: 2,
            bgcolor:
              theme.palette.mode === 'dark'
                ? alpha(theme.palette.background.paper, 0.92)
                : 'rgba(255,255,255,0.95)',
            boxShadow: '0 10px 28px rgba(15,23,42,0.12)',
          }}
        >
          <Stack direction="row" spacing={0.8}>
            {STEP_TYPES.slice(0, 6).map((type) => (
              <Tooltip key={type.value} title={type.description}>
                <Button size="small" onClick={() => createStep(type.value)} startIcon={<Iconify icon={type.icon} />}>
                  {type.label}
                </Button>
              </Tooltip>
            ))}
          </Stack>
        </Card>
      </Box>

      <Drawer anchor="right" open={Boolean(selectedStep)} onClose={() => setSelectedId(null)} PaperProps={{ sx: { width: 420, p: 2 } }}>
        {selectedStep && (
          <Stack spacing={1.5}>
            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Box>
                <Typography variant="subtitle1">Configurar paso</Typography>
                <Typography variant="caption" color="text.secondary">
                  Subflujo interno del agente
                </Typography>
              </Box>
              <IconButton onClick={() => setSelectedId(null)}>
                <Iconify icon="mingcute:close-line" />
              </IconButton>
            </Stack>
            <Divider />
            <TextField
              label="Tipo"
              select
              size="small"
              value={selectedStep.type}
              onChange={(event) => {
                const type = event.target.value as AgentStepType;
                dispatch(
                  updateStep({
                    id: selectedStep.id,
                    changes: {
                      type,
                      label: stepMeta(type).label,
                      description: stepMeta(type).description,
                      config: defaultConfigForStepType(type),
                    },
                  })
                );
              }}
            >
              {STEP_TYPES.map((type) => (
                <MenuItem key={type.value} value={type.value}>
                  {type.label}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Nombre"
              size="small"
              value={selectedStep.label}
              onChange={(event) => dispatch(updateStep({ id: selectedStep.id, changes: { label: event.target.value } }))}
            />
            <TextField
              label="Descripcion"
              multiline
              minRows={2}
              size="small"
              value={selectedStep.description}
              onChange={(event) =>
                dispatch(updateStep({ id: selectedStep.id, changes: { description: event.target.value } }))
              }
            />
            <Card variant="outlined" sx={{ p: 1.2, bgcolor: 'background.neutral' }}>
              <Stack spacing={1}>
                <Typography variant="subtitle2">Configuracion guiada</Typography>
                {(selectedStep.type === 'think' || selectedStep.type === 'plan') && (
                  <>
                    <TextField
                      label="Instruccion para el agente"
                      multiline
                      minRows={3}
                      size="small"
                      value={String(selectedConfig.prompt ?? '')}
                      helperText="Explica en lenguaje natural que debe analizar o planificar."
                      onChange={(event) => updateSelectedConfig('prompt', event.target.value)}
                    />
                    <TextField
                      label="Guardar resultado como"
                      size="small"
                      value={String(selectedConfig.outputKey ?? 'latest')}
                      onChange={(event) => updateSelectedConfig('outputKey', event.target.value)}
                    />
                  </>
                )}
                {(selectedStep.type === 'act' || selectedStep.type === 'tool_call') && (
                  <>
                    <TextField
                      label="Herramienta o integracion"
                      size="small"
                      value={String(selectedConfig.toolName ?? '')}
                      helperText="Debe existir como herramienta autorizada del asistente."
                      onChange={(event) => updateSelectedConfig('toolName', event.target.value)}
                    />
                    <TextField
                      label="Datos que recibe"
                      multiline
                      minRows={3}
                      size="small"
                      value={String(selectedConfig.inputTemplate ?? '{{input}}')}
                      onChange={(event) => updateSelectedConfig('inputTemplate', event.target.value)}
                    />
                  </>
                )}
                {selectedStep.type === 'observe' && (
                  <TextField
                    label="Que debe validar"
                    multiline
                    minRows={3}
                    size="small"
                    value={String(selectedConfig.successCriteria ?? 'La respuesta cumple el objetivo del usuario.')}
                    onChange={(event) => updateSelectedConfig('successCriteria', event.target.value)}
                  />
                )}
                {selectedStep.type === 'decide' && (
                  <>
                    <TextField
                      label="Modo de decision"
                      select
                      size="small"
                      value={String(selectedConfig.mode ?? 'contains')}
                      onChange={(event) => updateSelectedConfig('mode', event.target.value)}
                    >
                      <MenuItem value="contains">Contiene texto</MenuItem>
                      <MenuItem value="equals">Es igual a</MenuItem>
                      <MenuItem value="exists">Existe dato</MenuItem>
                    </TextField>
                    <TextField
                      label="Valor esperado"
                      size="small"
                      value={String(selectedConfig.matchValue ?? '')}
                      onChange={(event) => updateSelectedConfig('matchValue', event.target.value)}
                    />
                  </>
                )}
                {selectedStep.type === 'aggregate' && (
                  <>
                    <TextField
                      label="Estrategia"
                      select
                      size="small"
                      value={String(selectedConfig.strategy ?? 'concat')}
                      onChange={(event) => updateSelectedConfig('strategy', event.target.value)}
                    >
                      <MenuItem value="concat">Unir respuestas</MenuItem>
                      <MenuItem value="summary">Resumir</MenuItem>
                      <MenuItem value="json">Construir JSON</MenuItem>
                    </TextField>
                    <TextField
                      label="Separador"
                      size="small"
                      value={String(selectedConfig.separator ?? '\n---\n')}
                      onChange={(event) => updateSelectedConfig('separator', event.target.value)}
                    />
                  </>
                )}
                {selectedStep.type === 'human_review' && (
                  <TextField
                    label="Motivo de revision"
                    multiline
                    minRows={2}
                    size="small"
                    value={String(selectedConfig.reason ?? '')}
                    onChange={(event) => updateSelectedConfig('reason', event.target.value)}
                  />
                )}
              </Stack>
            </Card>
            <Button size="small" variant="text" onClick={() => setShowTechnicalConfig((value) => !value)}>
              {showTechnicalConfig ? 'Ocultar JSON tecnico' : 'Ver JSON tecnico'}
            </Button>
            {showTechnicalConfig && (
              <TextField
                label="Configuracion tecnica JSON"
                multiline
                minRows={7}
                size="small"
                value={JSON.stringify(selectedStep.config ?? {}, null, 2)}
                onChange={(event) => {
                  try {
                    dispatch(updateStep({ id: selectedStep.id, changes: { config: JSON.parse(event.target.value || '{}') } }));
                  } catch {
                    // The drawer allows partial JSON while the user edits.
                  }
                }}
              />
            )}
            <Alert severity="info">
              Este subflujo define como piensa y actua el agente. El la automatizacion principal solo lo llama como nodo.
            </Alert>
            <Button
              color="error"
              variant="outlined"
              onClick={() => {
                dispatch(removeStep(selectedStep.id));
                setSelectedId(null);
              }}
            >
              Eliminar paso
            </Button>
          </Stack>
        )}
      </Drawer>
    </Card>
  );
}


