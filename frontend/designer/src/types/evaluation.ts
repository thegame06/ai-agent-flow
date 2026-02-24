export interface FeatureFlagCheckRequest {
  agentId?: string;
  userId?: string;
  userSegments?: string[];
  metadata?: Record<string, string>;
}

export interface FeatureFlagCheckResponse {
  flagKey: string;
  isEnabled: boolean;
  checkedAt: string;
}

export interface SegmentRoutingRule {
  ruleName: string;
  matchSegments: string[];
  targetAgentId: string;
  priority: number;
  requireAllSegments: boolean;
}

export interface SegmentRoutingConfiguration {
  agentId: string;
  isEnabled: boolean;
  rules: SegmentRoutingRule[];
  defaultTargetAgentId?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface SegmentRoutingPreviewRequest {
  userId: string;
  userSegments: string[];
  metadata?: Record<string, string>;
}

export interface SegmentRoutingPreviewResponse {
  originalAgentId: string;
  selectedAgentId: string;
  wasRouted: boolean;
  matchedRuleName?: string;
  reason: string;
  evaluatedSegments: string[];
}
