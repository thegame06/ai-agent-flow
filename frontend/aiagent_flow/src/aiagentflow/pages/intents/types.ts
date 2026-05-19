// ----------------------------------------------------------------------
// Intent Management Types
// ----------------------------------------------------------------------

export interface Intent {
  id: string;
  key: string;
  name: string;
  description: string;
  category: string;
  examples: string[];
  synonyms: string[];
  confidence_threshold: number;
  priority: number;
  workflow_id?: string;
  workflow_name?: string;
  target_agent_id?: string;
  enabled: boolean;
  is_base_intent: boolean;
  created_at: string;
  updated_at: string;
}

export interface IntentStats {
  total_executions: number;
  avg_confidence: number;
  success_rate: number;
}

export interface IntentFormData {
  key: string;
  name: string;
  description: string;
  category: string;
  examples: string[];
  synonyms: string[];
  confidence_threshold: number;
  priority: number;
  workflow_id?: string;
  target_agent_id?: string;
  enabled: boolean;
}

export interface IntentFilter {
  category: string;
  enabled: string;
}

// ----------------------------------------------------------------------
// Workflow and Agent Types
// ----------------------------------------------------------------------

export interface Workflow {
  id: string;
  name: string;
  description?: string;
  status?: string;
}

export interface Agent {
  id: string;
  name: string;
  status?: string;
}

// ----------------------------------------------------------------------
// Playground Types
// ----------------------------------------------------------------------

export interface ClassificationResult {
  best_match: {
    intent_key: string;
    intent_name: string;
    description: string;
  };
  best_score: number;
  confidence: 'High' | 'Medium' | 'Low';
  all_candidates: IntentCandidate[];
  explanation_json: string;
  processing_time_ms: number;
}

export interface IntentCandidate {
  intent_key: string;
  intent_name: string;
  score: number;
  matched_features: string[];
}

export interface ExplanationData {
  decision: string;
  factors: Array<{
    name: string;
    contribution: number;
    details: string;
  }>;
  alternatives_considered: number;
}

// ----------------------------------------------------------------------
// Inbox Types
// ----------------------------------------------------------------------

export type ConversationState = 
  | 'AwaitingClassification' 
  | 'Classified' 
  | 'InProgress' 
  | 'Resolved' 
  | 'Abandoned';

export type ConfidenceLevel = 'High' | 'Medium' | 'Low';

export interface InboxConversation {
  id: string;
  tenant_id: string;
  channel: string;
  user_identifier: string;
  last_message: string;
  state: ConversationState;
  confidence: ConfidenceLevel;
  detected_intent_key?: string;
  created_at: string;
  updated_at: string;
  requires_human_review: boolean;
}

export interface InboxStats {
  total: number;
  awaiting_classification: number;
  classified: number;
  in_progress: number;
  resolved_today: number;
  avg_confidence: number;
  requires_review: number;
}

export interface InboxFilter {
  state: string;
  confidence: string;
}
