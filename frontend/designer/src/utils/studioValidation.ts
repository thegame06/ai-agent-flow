import { Edge, Node } from '@xyflow/react';
import { AgentNodeData } from '../types/agent';
import { DesignValidationIssue, SimulationStep } from '../types/studio';

const VAR_REF_REGEX = /\{\{\s*([a-zA-Z_][\w.-]*)\s*\}\}/g;

const getConnectedNodeIds = (nodes: Node<AgentNodeData>[], edges: Edge[]): Set<string> => {
  const connected = new Set<string>();
  edges.forEach((edge) => {
    connected.add(edge.source);
    connected.add(edge.target);
  });

  if (nodes.some((node) => node.id === 'start')) {
    connected.add('start');
  }

  return connected;
};

const getAvailableVariables = (nodes: Node<AgentNodeData>[]): Set<string> => {
  const vars = new Set<string>(['input']);

  nodes.forEach((node) => {
    const outputs = node.data.config && typeof node.data.config === 'object'
      ? (node.data.config.outputs as string[] | undefined)
      : undefined;

    outputs?.forEach((outputVar) => {
      if (typeof outputVar === 'string' && outputVar.trim().length > 0) {
        vars.add(outputVar.trim());
      }
    });
  });

  return vars;
};

export const validateStudioGraph = (nodes: Node<AgentNodeData>[], edges: Edge[]): DesignValidationIssue[] => {
  const issues: DesignValidationIssue[] = [];
  const outgoingByNode = new Map<string, number>();
  const connectedNodeIds = getConnectedNodeIds(nodes, edges);
  const availableVars = getAvailableVariables(nodes);

  edges.forEach((edge) => {
    outgoingByNode.set(edge.source, (outgoingByNode.get(edge.source) ?? 0) + 1);
  });

  nodes.forEach((node) => {
    if (!connectedNodeIds.has(node.id)) {
      issues.push({
        id: `isolated-${node.id}`,
        severity: 'warning',
        nodeId: node.id,
        message: `Node "${String(node.data.label ?? node.id)}" has no connections.`
      });
    }

    const hasNoExit = (outgoingByNode.get(node.id) ?? 0) === 0;
    const nodeType = String(node.data.type ?? node.type ?? '');
    const terminalNode = nodeType === 'output';
    if (hasNoExit && !terminalNode) {
      issues.push({
        id: `no-exit-${node.id}`,
        severity: 'error',
        nodeId: node.id,
        message: `Node "${String(node.data.label ?? node.id)}" has no outgoing transition.`
      });
    }

    const description = String(node.data.description ?? '');
    const refs = Array.from(description.matchAll(VAR_REF_REGEX)).map((match) => match[1]);
    refs.forEach((ref) => {
      if (!availableVars.has(ref)) {
        issues.push({
          id: `invalid-var-${node.id}-${ref}`,
          severity: 'error',
          nodeId: node.id,
          message: `Variable reference {{${ref}}} in "${String(node.data.label ?? node.id)}" is not defined.`
        });
      }
    });
  });

  return issues;
};

export const buildSimulationSteps = (nodes: Node<AgentNodeData>[], edges: Edge[]): SimulationStep[] => {
  if (nodes.length === 0) {
    return [];
  }

  const nodesById = new Map(nodes.map((node) => [node.id, node]));
  const startNode = nodesById.get('start') ?? nodes[0];
  const steps: SimulationStep[] = [];
  const visited = new Set<string>();
  let currentNode: Node<AgentNodeData> | undefined = startNode;
  const runtimeVariables: Record<string, string> = { input: 'customer asks for support' };

  while (currentNode && !visited.has(currentNode.id) && steps.length <= nodes.length) {
    visited.add(currentNode.id);

    const nodeOutputs = currentNode.data.config && typeof currentNode.data.config === 'object'
      ? (currentNode.data.config.outputs as string[] | undefined)
      : undefined;

    nodeOutputs?.forEach((outputVar, index) => {
      if (typeof outputVar === 'string' && outputVar.trim().length > 0) {
        runtimeVariables[outputVar.trim()] = `value_${steps.length + 1}_${index + 1}`;
      }
    });

    steps.push({
      nodeId: currentNode.id,
      label: String(currentNode.data.label ?? currentNode.id),
      variables: { ...runtimeVariables }
    });

    const nextEdge = edges.find((edge) => edge.source === currentNode?.id);
    currentNode = nextEdge ? nodesById.get(nextEdge.target) : undefined;
  }

  return steps;
};
