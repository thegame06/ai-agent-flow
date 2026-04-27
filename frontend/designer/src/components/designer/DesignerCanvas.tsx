import { useCallback, useMemo, useRef, DragEvent, useEffect } from 'react';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  Connection,
  Edge,
  EdgeChange,
  MarkerType,
  Node,
  NodeChange,
  useReactFlow
} from '@xyflow/react';
import { useSelector, useDispatch } from 'react-redux';
import { RootState } from '../../store';
import { setEdges, setNodes, selectNode, addNode } from '../../store/slices/designerSlice';
import { AgentNodeData } from '../../types/agent';
import { DesignValidationIssue } from '../../types/studio';
import { validateStudioGraph } from '../../utils/studioValidation';
import { StudioNode } from './StudioNode';

interface DesignerCanvasProps {
  onValidationChange?: (issues: DesignValidationIssue[]) => void;
}

const nodeTypes = {
  studioNode: StudioNode
};

const createStudioNode = (id: string, nodeType: string, label: string, position: { x: number; y: number }): Node<AgentNodeData> => ({
  id,
  type: 'studioNode',
  position,
  data: {
    label,
    type: nodeType,
    description: '',
    config: {
      outputs: [],
      transitions: ['default']
    }
  },
  style: {
    width: 240
  }
});

export const DesignerCanvas = ({ onValidationChange }: DesignerCanvasProps) => {
  const dispatch = useDispatch();
  const { graph } = useSelector((state: RootState) => state.designer);
  const { nodes, edges } = graph;
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { screenToFlowPosition } = useReactFlow();

  const validationIssues = useMemo(() => validateStudioGraph(nodes, edges), [nodes, edges]);

  useEffect(() => {
    onValidationChange?.(validationIssues);
  }, [onValidationChange, validationIssues]);

  const onConnect = useCallback(
    (params: Connection) => {
      if (!params.source || !params.target) {
        return;
      }
      const transitionLabel = params.sourceHandle === 'out' ? 'default' : 'custom';
      const nextEdge: Edge = {
        ...params,
        id: `${params.source}->${params.target}-${Date.now()}`,
        type: 'smoothstep',
        markerEnd: { type: MarkerType.ArrowClosed },
        label: transitionLabel,
        animated: false
      };
      dispatch(setEdges(addEdge(nextEdge, edges)));
    },
    [dispatch, edges]
  );

  const onNodesChange = useCallback(
    (changes: NodeChange[]) => dispatch(setNodes(applyNodeChanges(changes, nodes) as typeof nodes)),
    [dispatch, nodes]
  );

  const onEdgesChange = useCallback(
    (changes: EdgeChange[]) => dispatch(setEdges(applyEdgeChanges(changes, edges))),
    [dispatch, edges]
  );

  const onDragOver = useCallback((event: DragEvent) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
  }, []);

  const onDrop = useCallback(
    (event: DragEvent) => {
      event.preventDefault();

      const dataStr = event.dataTransfer.getData('application/reactflow');
      if (!dataStr) return;

      const { nodeType, label } = JSON.parse(dataStr) as { nodeType: string; label: string };

      const position = screenToFlowPosition({
        x: event.clientX,
        y: event.clientY
      });

      dispatch(addNode(createStudioNode(`node_${Date.now()}`, nodeType, label, position)));
    },
    [dispatch, screenToFlowPosition]
  );

  return (
    <div className="canvas-wrapper" ref={reactFlowWrapper} onDragOver={onDragOver} onDrop={onDrop}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={(_, node) => dispatch(selectNode(node.id))}
        onPaneClick={() => dispatch(selectNode(null))}
        fitView
        colorMode="dark"
      >
        <Background color="var(--border-light)" gap={24} size={1} />
        <Controls />
        <MiniMap
          style={{ background: 'var(--bg-secondary)', borderRadius: '12px', border: '1px solid var(--border-light)' }}
          nodeColor="#334155"
          maskColor="rgba(0, 0, 0, 0.3)"
        />
      </ReactFlow>
      <style>{`
        .canvas-wrapper { flex: 1; height: 100%; position: relative; background: var(--bg-primary); }
        .studio-node { background: var(--bg-secondary); border: 1px solid var(--border-strong); border-radius: 12px; padding: 12px; box-shadow: var(--shadow-md); min-width: 220px; }
        .studio-node.selected { border-color: var(--accent-primary); box-shadow: 0 0 0 1px var(--accent-primary); }
        .studio-node .title { color: var(--fg-primary); font-weight: 600; margin-bottom: 4px; }
        .studio-node .meta { color: var(--fg-muted); font-size: 11px; letter-spacing: 0.06em; margin-bottom: 8px; }
        .studio-node .transitions { display: flex; gap: 6px; flex-wrap: wrap; }
        .studio-node .chip { background: var(--accent-soft); color: var(--accent-primary); border-radius: 999px; padding: 2px 8px; font-size: 11px; }
        .react-flow__handle { width: 8px; height: 8px; background: var(--accent-primary); border: 2px solid var(--bg-primary); }
        .react-flow__edge-path { stroke: var(--border-strong); stroke-width: 2; }
      `}</style>
    </div>
  );
};
