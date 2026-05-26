import type {
  ToolOption,
  AgentOption,
  ModelOption,
  WorkflowStep,
  ChannelOption,
  WorkflowExecution,
  WorkflowAuditEvent,
  WorkflowDefinition,
  WorkflowActivityNode,
  ConnectTemplateOption,
  WorkflowRuntimeMetrics,
  AssistantWizardMetrics,
  WorkflowIntegrationStatus,
  WorkflowActivityCatalogEntry,
} from '../types';

export type WorkflowEditorDraft = {
  id: string;
  name: string;
  triggerEventName: string;
  definitionJson: string;
};

export type WorkflowEditorState = {
  draft: WorkflowEditorDraft;
  activities: WorkflowActivityNode[];
  selectedWorkflowId: string | null;
  isDirty: boolean;
};

export type WorkflowRuntimeState = {
  loading: boolean;
  saving: boolean;
  running: boolean;
  error: string | null;
  workflows: WorkflowDefinition[];
  executions: WorkflowExecution[];
  steps: WorkflowStep[];
  stepsOpen: boolean;
  metrics: WorkflowRuntimeMetrics | null;
  wizardMetrics: AssistantWizardMetrics | null;
  auditEvents: WorkflowAuditEvent[];
  activityCatalog: WorkflowActivityCatalogEntry[];
  availableModels: ModelOption[];
  availableTools: ToolOption[];
  availableAgents: AgentOption[];
  availableChannels: ChannelOption[];
  integrations: WorkflowIntegrationStatus[];
  connectTemplates: ConnectTemplateOption[];
};
