import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { Edge, Node } from '@xyflow/react';
import { AgentDesignerDto, AgentNodeData, DesignerStepDto } from '../../types/agent';

export interface PersistedGraphState {
  nodes: Node<AgentNodeData>[];
  edges: Edge[];
}

interface DesignerState {
  agentId: string | null;
  graph: PersistedGraphState;
  selectedNodeId: string | null;
  isDirty: boolean;
  isLoading: boolean;
  saveError: string | null;
  agentName: string;
  description: string;
  version: string;
}

const initialNodes: Node<AgentNodeData>[] = [
  {
    id: 'start',
    type: 'studioNode',
    position: { x: 250, y: 50 },
    data: { label: 'User Request Received', type: 'think', description: '', config: { outputs: ['intent'], transitions: ['success'] } },
    style: { background: '#1e293b', color: '#fff', border: '1px solid #334155', width: 180 }
  }
];

const initialState: DesignerState = {
  agentId: null,
  graph: {
    nodes: initialNodes,
    edges: []
  },
  selectedNodeId: null,
  isDirty: false,
  isLoading: false,
  saveError: null,
  agentName: 'NewAgent',
  description: '',
  version: '1.0.0'
};

const nodeToStep = (node: Node<AgentNodeData>, allEdges: Edge[]): DesignerStepDto => ({
  id: node.id,
  type: String(node.data.type ?? node.type ?? 'think'),
  label: String(node.data.label ?? ''),
  description: String(node.data.description ?? ''),
  config: (node.data.config as Record<string, unknown>) ?? {},
  position: {
    x: node.position.x,
    y: node.position.y
  },
  connections: allEdges.filter((edge) => edge.source === node.id).map((edge) => edge.target)
});

const applyDefaultStyle = (node: Node<AgentNodeData>): Node<AgentNodeData> => ({
  ...node,
  style: node.style ?? {
    background: 'var(--bg-secondary)',
    color: 'var(--fg-primary)',
    border: '1px solid var(--border-strong)',
    borderRadius: '12px',
    padding: '12px',
    fontSize: '13px',
    width: 200,
    boxShadow: 'var(--shadow-md)'
  }
});

export const mapDtoToGraph = (dto: AgentDesignerDto): PersistedGraphState => {
  const nodes: Node<AgentNodeData>[] = dto.steps.map((step) =>
    applyDefaultStyle({
      id: step.id,
      type: 'studioNode',
      position: {
        x: step.position.x,
        y: step.position.y
      },
      data: {
        label: step.label,
        type: step.type,
        description: step.description,
        config: step.config
      }
    })
  );

  const edges: Edge[] = dto.steps.flatMap((step) =>
    step.connections.map((targetId) => ({
      id: `${step.id}->${targetId}`,
      source: step.id,
      target: targetId,
      animated: false
    }))
  );

  return {
    nodes: nodes.length > 0 ? nodes : initialNodes,
    edges
  };
};

export const mapGraphToDesignerDto = (state: DesignerState): AgentDesignerDto => ({
  name: state.agentName,
  description: state.description,
  status: 'Draft',
  version: state.version,
  brain: {
    primaryModel: 'gpt-4o',
    fallbackModel: 'gpt-4o-mini',
    provider: 'OpenAI',
    systemPrompt: '',
    temperature: 0.7,
    maxResponseTokens: 4096
  },
  loop: {
    maxSteps: 25,
    timeoutPerStepMs: 30000,
    maxTokensPerExecution: 100000,
    maxRetries: 3,
    enablePromptInjectionGuard: true,
    enablePIIProtection: true,
    requireHumanApproval: false,
    humanApprovalThreshold: 'high_risk',
    allowParallelToolCalls: false,
    plannerType: 'ReAct',
    runtimeMode: 'Autonomous'
  },
  memory: {
    workingMemory: true,
    longTermMemory: false,
    vectorMemory: false,
    auditMemory: true
  },
  session: {
    enableThreads: false,
    defaultThreadTtlHours: 168,
    maxTurnsPerThread: 100,
    contextWindowSize: 10,
    autoCreateThread: true,
    enableSummarization: false,
    threadKeyPattern: '{agentName}-{guid}'
  },
  steps: state.graph.nodes.map((node) => nodeToStep(node, state.graph.edges)),
  tools: [],
  tags: []
});

const designerSlice = createSlice({
  name: 'designer',
  initialState,
  reducers: {
    setNodes: (state, action: PayloadAction<Node<AgentNodeData>[]>) => {
      state.graph.nodes = action.payload;
      state.isDirty = true;
    },
    setEdges: (state, action: PayloadAction<Edge[]>) => {
      state.graph.edges = action.payload;
      state.isDirty = true;
    },
    selectNode: (state, action: PayloadAction<string | null>) => {
      state.selectedNodeId = action.payload;
    },
    addNode: (state, action: PayloadAction<Node<AgentNodeData>>) => {
      state.graph.nodes.push(action.payload);
      state.isDirty = true;
    },
    updateNodeData: (state, action: PayloadAction<{ id: string; data: Partial<AgentNodeData> }>) => {
      const node = state.graph.nodes.find((n) => n.id === action.payload.id);
      if (node) {
        node.data = { ...node.data, ...action.payload.data };
        state.isDirty = true;
      }
    },
    setAgentName: (state, action: PayloadAction<string>) => {
      state.agentName = action.payload;
      state.isDirty = true;
    },
    hydrateFromAgentDto: (state, action: PayloadAction<AgentDesignerDto & { id?: string }>) => {
      state.agentId = action.payload.id ?? null;
      state.agentName = action.payload.name;
      state.description = action.payload.description;
      state.version = action.payload.version;
      state.graph = mapDtoToGraph(action.payload);
      state.selectedNodeId = null;
      state.isDirty = false;
      state.saveError = null;
    },
    setLoading: (state, action: PayloadAction<boolean>) => {
      state.isLoading = action.payload;
    },
    setSaveError: (state, action: PayloadAction<string | null>) => {
      state.saveError = action.payload;
    },
    markSaved: (state) => {
      state.isDirty = false;
      state.saveError = null;
    }
  }
});

export const {
  setNodes,
  setEdges,
  selectNode,
  addNode,
  updateNodeData,
  setAgentName,
  hydrateFromAgentDto,
  setLoading,
  setSaveError,
  markSaved
} = designerSlice.actions;

export default designerSlice.reducer;
