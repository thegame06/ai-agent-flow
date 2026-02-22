// Agent Designer Types
// Following the DSL Composable Híbrido pattern from project architecture

export interface AgentStep {
  id: string;
  type: 'think' | 'plan' | 'act' | 'observe' | 'decide' | 'tool_call' | 'human_review';
  label: string;
  description: string;
  config: Record<string, unknown>;
  position: { x: number; y: number };
  connections: string[]; // IDs of next steps
}

export interface AgentToolBinding {
  toolId: string;
  toolName: string;
  version: string;
  riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
  permissions: string[];
}

export interface AgentMemoryConfig {
  workingMemory: boolean;
  longTermMemory: boolean;
  vectorMemory: boolean;
  auditMemory: boolean; // Always true — immutable
}

export interface AgentGuardrails {
  maxSteps: number;
  timeoutPerStepMs: number;
  maxTokensPerExecution: number;
  maxRetries: number;
  enablePromptInjectionGuard: boolean;
  enablePIIProtection: boolean;
  hitl: {
    enabled: boolean;
    requireReviewOnAllToolCalls: boolean;
    requireReviewOnPolicyEscalation: boolean;
    confidenceThreshold: number;
  };
}

export interface AgentModelConfig {
  primaryModel: string;
  fallbackModel: string;
  temperature: number;
  maxResponseTokens: number;
}

export interface AgentDefinitionDraft {
  id?: string;
  name: string;
  description: string;
  version: string;
  status: 'Draft' | 'Published' | 'Archived';
  steps: AgentStep[];
  tools: AgentToolBinding[];
  memory: AgentMemoryConfig;
  guardrails: AgentGuardrails;
  model: AgentModelConfig;
  systemPrompt: string;
  tags: string[];
}

export const DEFAULT_ENGINE_STEPS: AgentStep[] = [
  {
    id: 'step-1',
    type: 'think',
    label: 'Identify Intent & Think',
    description: 'The agent analyzes input and decides the next step.',
    config: {},
    position: { x: 0, y: 0 },
    connections: ['step-2'],
  },
  {
    id: 'step-2',
    type: 'act',
    label: 'Execute Tool',
    description: 'The agent calls the authorized tools.',
    config: {},
    position: { x: 0, y: 100 },
    connections: ['step-3'],
  },
  {
    id: 'step-3',
    type: 'observe',
    label: 'Observe Result',
    description: 'The agent evaluates the tool output.',
    config: {},
    position: { x: 0, y: 200 },
    connections: [],
  },
];

export const DEFAULT_AGENT_DRAFT: AgentDefinitionDraft = {
  name: '',
  description: '',
  version: '1.0.0',
  status: 'Draft',
  steps: [...DEFAULT_ENGINE_STEPS],
  tools: [],
  memory: {
    workingMemory: true,
    longTermMemory: false,
    vectorMemory: false,
    auditMemory: true,
  },
  guardrails: {
    maxSteps: 25,
    timeoutPerStepMs: 30000,
    maxTokensPerExecution: 100000,
    maxRetries: 3,
    enablePromptInjectionGuard: true,
    enablePIIProtection: true,
    hitl: {
      enabled: false,
      requireReviewOnAllToolCalls: false,
      requireReviewOnPolicyEscalation: true,
      confidenceThreshold: 0.7,
    }
  },
  model: {
    primaryModel: 'gpt-4o',
    fallbackModel: 'gpt-4o-mini',
    temperature: 0.7,
    maxResponseTokens: 4096,
  },
  systemPrompt: '',
  tags: [],
};
