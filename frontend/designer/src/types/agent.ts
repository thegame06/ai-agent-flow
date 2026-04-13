export enum ThinkDecision {
  UseTool = 'UseTool',
  ProvideFinalAnswer = 'ProvideFinalAnswer',
  Checkpoint = 'Checkpoint',
  RequestMoreContext = 'RequestMoreContext'
}

export enum ToolRiskLevel {
  Low = 'Low',
  Medium = 'Medium',
  High = 'High',
  Critical = 'Critical'
}

export interface AgentNodeData {
  [key: string]: unknown;
  label: string;
  type?: string;
  description?: string;
  config?: Record<string, unknown>;
  riskLevel?: ToolRiskLevel;
}

export interface DesignerStepDto {
  id: string;
  type: string;
  label: string;
  description: string;
  config: Record<string, unknown>;
  position: {
    x: number;
    y: number;
  };
  connections: string[];
}

export interface AgentDesignerDto {
  id?: string;
  name: string;
  description: string;
  status: string;
  version: string;
  brain: {
    primaryModel: string;
    fallbackModel: string;
    provider: string;
    systemPrompt: string;
    temperature: number;
    maxResponseTokens: number;
  };
  loop: {
    maxSteps: number;
    timeoutPerStepMs: number;
    maxTokensPerExecution: number;
    maxRetries: number;
    enablePromptInjectionGuard: boolean;
    enablePIIProtection: boolean;
    requireHumanApproval: boolean;
    humanApprovalThreshold: string;
    allowParallelToolCalls: boolean;
    plannerType: string;
    runtimeMode: string;
  };
  memory: {
    workingMemory: boolean;
    longTermMemory: boolean;
    vectorMemory: boolean;
    auditMemory: boolean;
  };
  session: {
    enableThreads: boolean;
    defaultThreadTtlHours: number;
    maxTurnsPerThread: number;
    contextWindowSize: number;
    autoCreateThread: boolean;
    enableSummarization: boolean;
    threadKeyPattern: string;
  };
  steps: DesignerStepDto[];
  tools: Array<{
    toolId: string;
    toolName: string;
    version: string;
    riskLevel: string;
    permissions: string[];
  }>;
  tags: string[];
}

export interface AgentExecutionStepDto {
  id: string;
  stepType: string;
  iteration: number;
  durationMs: number;
  startedAt: string;
  completedAt?: string;
  toolId?: string;
  toolName?: string;
  inputJson?: string;
  outputJson?: string;
  llmPrompt?: string;
  llmResponse?: string;
  tokensUsed?: number;
  isSuccess: boolean;
  errorMessage?: string;
}

export interface AgentExecutionDetailsDto {
  id: string;
  status: string;
  output?: {
    finalResponse: string;
    totalTokensUsed: number;
  };
  steps: AgentExecutionStepDto[];
}

export interface PreviewExecutionResponse {
  success: boolean;
  executionId: string;
  status: string;
  finalResponse?: string;
  totalSteps: number;
  totalTokensUsed: number;
  durationMs: number;
  errorCode?: string;
  errorMessage?: string;
}

export interface PreviewTimelineItem {
  id: string;
  type: string;
  toolName?: string;
  durationMs: number;
  tokensUsed?: number;
  inputJson?: string;
  outputJson?: string;
  llmPrompt?: string;
  llmResponse?: string;
  isSuccess: boolean;
  errorMessage?: string;
}

export interface AgentDefinition {
  id: string;
  name: string;
  description: string;
  version: number;
  systemPrompt: string;
  loopConfig: {
    maxIterations: number;
    plannerType: string;
    toolCallTimeout: string;
  };
}
