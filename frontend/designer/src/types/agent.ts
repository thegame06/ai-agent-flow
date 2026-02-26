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
  config?: Record<string, unknown>;
  riskLevel?: ToolRiskLevel;
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
