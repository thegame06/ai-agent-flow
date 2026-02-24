export enum PolicyCheckpoint {
  PreAgent = 0,
  PreLLM = 1,
  PostLLM = 2,
  PreTool = 3,
  PostTool = 4,
  PreResponse = 5
}

export enum PolicyAction {
  Allow = 'Allow',
  Block = 'Block',
  Warn = 'Warn',
  Escalate = 'Escalate',
  Shadow = 'Shadow'
}

export enum PolicySeverity {
  Info = 'Info',
  Low = 'Low',
  Medium = 'Medium',
  High = 'High',
  Critical = 'Critical'
}

export interface PolicyDefinition {
  policyId: string;
  description: string;
  appliesAt: PolicyCheckpoint;
  policyType: string;
  action: PolicyAction;
  severity: PolicySeverity;
  isEnabled: boolean;
  config: Record<string, string>;
  targetSegments: string[];
}

export interface PolicySetDefinition {
  policySetId: string;
  version: string;
  tenantId: string;
  isPublished: boolean;
  policies: PolicyDefinition[];
  name?: string; // Derived in API
  description?: string; // Description of the policy set
  createdAt?: string; // Derived in API
}

export interface PolicyEvaluationContext {
  tenantId: string;
  agentKey: string;
  agentVersion: string;
  policySetId: string;
  executionId: string;
  userId: string;
  checkpoint: PolicyCheckpoint;
  toolName?: string;
  llmResponse?: string;
  finalResponse?: string;
  userMessage?: string;
  toolInputJson?: string;
  toolOutputJson?: string;
  userSegments: string[];
  metadata: Record<string, string>;
}
