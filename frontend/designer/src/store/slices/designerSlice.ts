import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { Node, Edge } from '@xyflow/react';
import { AgentNodeData } from '../../types/agent';

interface DesignerState {
  nodes: Node<AgentNodeData>[];
  edges: Edge[];
  selectedNodeId: string | null;
  isDirty: boolean;
  agentName: string;
  version: string;
}

const initialState: DesignerState = {
  nodes: [
    { 
      id: 'start', 
      type: 'input',
      position: { x: 250, y: 50 }, 
      data: { label: 'User Request Received' },
      style: { background: '#1e293b', color: '#fff', border: '1px solid #334155', width: 180 }
    }
  ],
  edges: [],
  selectedNodeId: null,
  isDirty: false,
  agentName: 'NewAgent',
  version: '1.0.0'
};

const designerSlice = createSlice({
  name: 'designer',
  initialState,
  reducers: {
    setNodes: (state, action: PayloadAction<Node<AgentNodeData>[]>) => {
      state.nodes = action.payload;
    },
    setEdges: (state, action: PayloadAction<Edge[]>) => {
      state.edges = action.payload;
    },
    selectNode: (state, action: PayloadAction<string | null>) => {
      state.selectedNodeId = action.payload;
    },
    addNode: (state, action: PayloadAction<Node<AgentNodeData>>) => {
      state.nodes.push(action.payload);
      state.isDirty = true;
    },
    updateNodeData: (state, action: PayloadAction<{ id: string, data: Partial<AgentNodeData> }>) => {
      const node = state.nodes.find((n) => n.id === action.payload.id);
      if (node) {
        node.data = { ...node.data, ...action.payload.data };
        state.isDirty = true;
      }
    },
    setAgentName: (state, action: PayloadAction<string>) => {
      state.agentName = action.payload;
      state.isDirty = true;
    }
  }
});

export const { setNodes, setEdges, selectNode, addNode, updateNodeData, setAgentName } = designerSlice.actions;
export default designerSlice.reducer;
