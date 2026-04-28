import { Edge, Node } from '@xyflow/react';
import { AgentNodeData } from './agent';

export type ValidationSeverity = 'error' | 'warning';
export type TransitionType = 'success' | 'error' | 'timeout' | 'escalation';

export interface StudioNodeTemplate {
  type: string;
  label: string;
  color: string;
}

export interface DesignValidationIssue {
  id: string;
  severity: ValidationSeverity;
  nodeId?: string;
  message: string;
}

export interface SimulationStep {
  nodeId: string;
  label: string;
  nodeType: string;
  transition?: TransitionType;
  variables: Record<string, string>;
  context: Record<string, string>;
}

export type StudioPermission = 'studio.view' | 'studio.edit' | 'studio.publish';

export interface StudioGraph {
  nodes: Node<AgentNodeData>[];
  edges: Edge[];
}
