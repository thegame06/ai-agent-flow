import { Edge, Node } from '@xyflow/react';
import { AgentNodeData } from '../types/agent';
import { DesignValidationIssue, SimulationStep, TransitionType } from '../types/studio';

const VAR_REF_REGEX = /\{\{\s*([a-zA-Z_][\w.-]*)\s*\}\}/g;
const ALLOWED_TRANSITIONS: TransitionType[] = ['success', 'error', 'timeout', 'escalation'];

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
  const incomingByNode = new Map<string, number>();
  const connectedNodeIds = getConnectedNodeIds(nodes, edges);
  const availableVars = getAvailableVariables(nodes);
  const nodesById = new Set(nodes.map((node) => node.id));

  edges.forEach((edge) => {
    outgoingByNode.set(edge.source, (outgoingByNode.get(edge.source) ?? 0) + 1);
    incomingByNode.set(edge.target, (incomingByNode.get(edge.target) ?? 0) + 1);

    if (!nodesById.has(edge.source) || !nodesById.has(edge.target)) {
      issues.push({
        id: `broken-edge-${edge.id}`,
        severity: 'error',
        message: `La transición ${edge.id} apunta a un nodo inexistente.`
      });
    }

    if (edge.label && !ALLOWED_TRANSITIONS.includes(String(edge.label) as TransitionType)) {
      issues.push({
        id: `invalid-transition-${edge.id}`,
        severity: 'error',
        nodeId: edge.source,
        message: `La transición "${String(edge.label)}" no es válida. Usa success/error/timeout/escalation.`
      });
    }
  });

  nodes.forEach((node) => {
    if (!connectedNodeIds.has(node.id)) {
      issues.push({
        id: `isolated-${node.id}`,
        severity: 'error',
        nodeId: node.id,
        message: `Nodo huérfano "${String(node.data.label ?? node.id)}" sin conexiones.`
      });
    }

    const hasNoExit = (outgoingByNode.get(node.id) ?? 0) === 0;
    const nodeType = String(node.data.type ?? node.type ?? '');
    const terminalNode = nodeType === 'output' || nodeType === 'end' || nodeType === 'fin';
    if (hasNoExit && !terminalNode) {
      issues.push({
        id: `no-exit-${node.id}`,
        severity: 'error',
        nodeId: node.id,
        message: `Nodo "${String(node.data.label ?? node.id)}" sin transición de salida.`
      });
    }
    const hasNoEntry = (incomingByNode.get(node.id) ?? 0) === 0 && node.id !== 'start';
    if (hasNoEntry) {
      issues.push({
        id: `no-entry-${node.id}`,
        severity: 'error',
        nodeId: node.id,
        message: `Nodo "${String(node.data.label ?? node.id)}" no es alcanzable desde Inicio.`
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
          message: `Variable rota {{${ref}}} en "${String(node.data.label ?? node.id)}".`
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
    const nextEdge = edges.find((edge) => edge.source === currentNode?.id);

    steps.push({
      nodeId: currentNode.id,
      label: String(currentNode.data.label ?? currentNode.id),
      nodeType: String(currentNode.data.type ?? 'unknown'),
      transition: nextEdge?.label ? (String(nextEdge.label) as TransitionType) : undefined,
      variables: { ...runtimeVariables },
      context: {
        currentNodeId: currentNode.id,
        outgoingTransition: nextEdge?.label ? String(nextEdge.label) : 'none'
      }
    });
    currentNode = nextEdge ? nodesById.get(nextEdge.target) : undefined;
  }

  return steps;
};
