export type WorkflowDefinition = {
  id: string;
  name: string;
  triggerEventName: string;
  runtimeKind?: string;
  version: number;
  status: 'Draft' | 'Published' | 'Archived' | string;
  definitionJson: string;
  updatedAt: string;
  updatedBy: string;
};

export type WorkflowExecution = {
  id: string;
  workflowDefinitionId: string;
  triggerEventName: string;
  correlationId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | string;
  error?: string | null;
  createdAt: string;
  updatedAt: string;
  requestedBy: string;
};

export type WorkflowStep = {
  id: string;
  activityType: string;
  activityName: string;
  status: string;
  error?: string | null;
  startedAt: string;
  completedAt?: string | null;
};

export type WorkflowAuditEvent = {
  id: string;
  executionId: string;
  workflowId: string;
  actor: string;
  correlationId: string;
  occurredAt: string;
  eventJson: string;
};

export type WorkflowRuntimeActivityMetric = {
  activityType: string;
  total: number;
  succeeded: number;
  avgLatencyMs: number;
};

export type WorkflowRuntimeMetrics = {
  total: number;
  successRate: number;
  failureRate: number;
  avgLatencyMs: number;
  window?: '24h' | '7d' | '30d' | string;
  windowStart?: string;
  activityMetrics: WorkflowRuntimeActivityMetric[];
  continuitySignals?: {
    windowSize: number;
    loopDetected: number;
    repromptBlocked: number;
    contextWiring: number;
    escalatedHuman: number;
    providerResolutionByRole?: {
      stt?: ProviderResolutionSignal;
      tts?: ProviderResolutionSignal;
      callControl?: ProviderResolutionSignal;
      reasoning?: ProviderResolutionSignal;
    };
    rates?: {
      loopPerContext: number;
      escalationPerContext: number;
      repromptBlockedPerContext: number;
    };
  };
};

export type ProviderResolutionSignal = {
  primary: number;
  fallback: number;
  failed: number;
  providers: string[];
};

export type AssistantWizardMetrics = {
  tenantId: string;
  generatedAt: string;
  windowSize: number;
  funnel: {
    sessionsCreated: number;
    questionsAnswered: number;
    sessionsCompleted: number;
    sessionsMaterialized: number;
  };
  conversion: {
    completionRate: number;
    materializationRate: number;
  };
  dropoff?: {
    language: number;
    task: number;
    audience: number;
    tone: number;
  };
};

export type AiAgentNodeConfig = {
  agentId?: string;
  agentName?: string;
  agentVersion?: string | number;
  model: string;
  instructions: string;
  tools: string[];
  context: string;
  knowledge: string[];
  input?: string;
  outputVariable?: string;
  fallbackModel?: string;
  maxLatencyMs?: number;
  maxCostUsd?: number;
  dlpEnabled?: boolean;
  temperature: number;
  maxTokens: number;
};

export type WorkflowActivityNode = {
  id: string;
  type: string;
  position?: { x: number; y: number };
  name?: string;
  next?: string;
  onSuccess?: string;
  onFailure?: string;
  timeoutMs?: number;
  retryCount?: number;
  retryDelayMs?: number;
  config?: Record<string, string>;
  aiAgent?: AiAgentNodeConfig;
};

export type WorkflowStartIntent = {
  id: string;
  label: string;
  description?: string;
  examples?: string[];
  eventName: string;
  triggerSource?: 'message' | 'button' | 'webhook' | 'campaign' | 'voice';
  channelType?: string;
  confidenceThreshold?: number;
};

export type WorkflowActivityCatalogEntry = {
  typeName: string;
  displayName: string;
  inputSchema?: Record<string, string>;
};

export type ModelOption = {
  modelId: string;
  displayName?: string;
};

export type ToolOption = {
  key: string;
  displayName?: string;
};

export type WorkflowIntegrationStatus = {
  key: string;
  displayName: string;
  category: 'channel' | 'connection' | 'extension';
  enabled: boolean;
  connected: boolean;
  secretsConfigured: boolean;
  capabilities: string[];
  detail?: string;
};

export type ConnectTemplateOption = {
  id: string;
  name: string;
  channel: string;
  body: string;
};

export type AgentOption = {
  id: string;
  name: string;
  description?: string;
  status: string;
  version?: string | number;
  tags?: string[];
  updatedAt?: string;
  stepsCount?: number;
  toolsCount?: number;
  primaryModel?: string;
  provider?: string;
  isSystemAgent?: boolean;
  systemRole?: string;
};

export type ChannelOption = {
  id: string;
  name: string;
  type: string;
  status: string;
  config: Record<string, string>;
  routerAgentId?: string;
  defaultAgentId?: string;
  intentAgents?: string[];
  // legacy
  routingAgents?: string[];
};

export type SchemaFieldRule = {
  key: string;
  required: boolean;
  defaultValue?: string;
};
