import { useCallback, useRef, DragEvent } from 'react';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  Connection,
  EdgeChange,
  NodeChange,
  useReactFlow
} from '@xyflow/react';
import { useSelector, useDispatch } from 'react-redux';
import { RootState } from '../../store';
import { setEdges, setNodes, selectNode, addNode } from '../../store/slices/designerSlice';

export const DesignerCanvas = () => {
  const dispatch = useDispatch();
  const { graph } = useSelector((state: RootState) => state.designer);
  const { nodes, edges } = graph;
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { screenToFlowPosition } = useReactFlow();

  const onConnect = useCallback(
    (params: Connection) => dispatch(setEdges(addEdge(params, edges))),
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

      const { nodeType, label } = JSON.parse(dataStr);

      const position = screenToFlowPosition({
        x: event.clientX,
        y: event.clientY
      });

      const newNode = {
        id: `node_${Date.now()}`,
        type: nodeType,
        position,
        data: { label: label, type: nodeType, description: '', config: {} },
        style: {
          background: 'var(--bg-secondary)',
          color: 'var(--fg-primary)',
          border: '1px solid var(--border-strong)',
          borderRadius: '12px',
          padding: '12px',
          fontSize: '13px',
          width: 200,
          boxShadow: 'var(--shadow-md)'
        }
      };

      dispatch(addNode(newNode));
    },
    [dispatch, screenToFlowPosition]
  );

  return (
    <div className="canvas-wrapper" ref={reactFlowWrapper} onDragOver={onDragOver} onDrop={onDrop}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
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
        .react-flow__handle { width: 8px; height: 8px; background: var(--accent-primary); border: 2px solid var(--bg-primary); }
        .react-flow__edge-path { stroke: var(--border-strong); stroke-width: 2; }
      `}</style>
    </div>
  );
};
