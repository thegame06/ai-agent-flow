import { Edge, Node } from '@xyflow/react';
import { AgentNodeData } from './agent';

export type ValidationSeverity = 'error' | 'warning';

export interface DesignValidationIssue {
  id: string;
  severity: ValidationSeverity;
  nodeId?: string;
  message: string;
}

export interface SimulationStep {
  nodeId: string;
  label: string;
  variables: Record<string, string>;
}

export type StudioPermission = 'studio.view' | 'studio.edit' | 'studio.publish';

export interface StudioGraph {
  nodes: Node<AgentNodeData>[];
  edges: Edge[];
}
