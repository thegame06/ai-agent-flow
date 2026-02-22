import '@xyflow/react/dist/style.css';

import type { AppDispatch } from 'src/aiagentflow/store';
import type { Edge, Node, Connection } from '@xyflow/react';

import { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import {
  addEdge,
  MiniMap,
  Controls,
  ReactFlow,
  Background,
  MarkerType,
  useEdgesState,
  useNodesState,
} from '@xyflow/react';

import { Box, alpha, useTheme } from '@mui/material';

import { updateStep } from './designerSlice';

import type { AgentStep } from './types';

// ─── Custom Node Component ────────────────────────────────────────────────
function StepNode({ data }: { data: any }) {
  const theme = useTheme();

  const typeConfig: Record<string, { icon: string; color: string }> = {
    think: { icon: '🧠', color: '#7C4DFF' },
    plan: { icon: '🗺️', color: '#00BCD4' },
    act: { icon: '⚡', color: '#FF9800' },
    observe: { icon: '👁️', color: '#4CAF50' },
    decide: { icon: '🔀', color: '#E91E63' },
    tool_call: { icon: '🔧', color: '#607D8B' },
    human_review: { icon: '✋', color: '#795548' },
  };

  const config = typeConfig[data.type] || { icon: '❓', color: '#999' };

  return (
    <Box
      sx={{
        minWidth: 180,
        padding: 2,
        borderRadius: 2,
        bgcolor: 'background.paper',
        borderLeft: `4px solid ${config.color}`,
        boxShadow: theme.shadows[4],
        transition: 'all 0.2s',
        '&:hover': {
          boxShadow: theme.shadows[12],
          transform: 'translateY(-2px)',
        },
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 1 }}>
        <Box sx={{ fontSize: 24 }}>{config.icon}</Box>
        <Box>
          <Box sx={{ fontWeight: 700, fontSize: 14, color: 'text.primary' }}>
            {data.label}
          </Box>
          <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
            {data.type.replace('_', ' ').toUpperCase()}
          </Box>
        </Box>
      </Box>
      {data.description && (
        <Box sx={{ fontSize: 12, color: 'text.secondary', mt: 1 }}>
          {data.description}
        </Box>
      )}
    </Box>
  );
}

const nodeTypes = {
  stepNode: StepNode,
};

// ─── Canvas Component ─────────────────────────────────────────────────────
interface AgentFlowCanvasProps {
  steps: AgentStep[];
}

export default function AgentFlowCanvas({ steps }: AgentFlowCanvasProps) {
  const theme = useTheme();
  const dispatch = useDispatch<AppDispatch>();

  // Convert steps to ReactFlow nodes
  const initialNodes: Node[] = steps.map((step, idx) => ({
    id: step.id,
    type: 'stepNode',
    position: step.position || { x: 100, y: idx * 100 },
    data: {
      label: step.label,
      type: step.type,
      description: step.description,
    },
  }));

  // Convert step connections to ReactFlow edges
  const initialEdges: Edge[] = [];
  steps.forEach((step) => {
    step.connections?.forEach((targetId) => {
      initialEdges.push({
        id: `${step.id}-${targetId}`,
        source: step.id,
        target: targetId,
        type: 'smoothstep',
        animated: true,
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: theme.palette.primary.main,
        },
        style: {
          stroke: theme.palette.primary.main,
          strokeWidth: 2,
        },
      });
    });
  });

  const [nodes, , onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);

  const onConnect = useCallback(
    (connection: Connection) => {
      setEdges((eds) => addEdge(connection, eds));
      // TODO: Update redux state with new connection
    },
    [setEdges]
  );

  const onNodeDragStop = useCallback(
    (_event: any, node: Node) => {
      dispatch(
        updateStep({
          id: node.id,
          changes: { position: node.position },
        })
      );
    },
    [dispatch]
  );

  return (
    <Box
      sx={{
        width: '100%',
        height: '600px',
        border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
        borderRadius: 2,
        overflow: 'hidden',
        bgcolor: alpha(theme.palette.grey[500], 0.02),
      }}
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeDragStop={onNodeDragStop}
        nodeTypes={nodeTypes}
        fitView
        snapToGrid
        snapGrid={[15, 15]}
        defaultEdgeOptions={{
          type: 'smoothstep',
          animated: true,
        }}
      >
        <Background gap={16} size={1} />
        <Controls />
        <MiniMap
          nodeColor={(node) => {
            const typeColors: Record<string, string> = {
              think: '#7C4DFF',
              plan: '#00BCD4',
              act: '#FF9800',
              observe: '#4CAF50',
              decide: '#E91E63',
              tool_call: '#607D8B',
              human_review: '#795548',
            };
            const nodeType = (node.data as any)?.type as string;
            return typeColors[nodeType] || '#999';
          }}
          maskColor={alpha(theme.palette.background.paper, 0.8)}
        />
      </ReactFlow>
    </Box>
  );
}
