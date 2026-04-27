import { describe, expect, it } from 'vitest';
import { Edge, Node } from '@xyflow/react';
import { AgentNodeData } from './types/agent';
import { computeTotalTokens, mapStepToTimeline } from './pages/sandbox/sandbox-utils';
import { mapGraphToDesignerDto } from './store/slices/designerSlice';
import { buildSimulationSteps, validateStudioGraph } from './utils/studioValidation';

describe('designer -> preview -> inspect flow helpers', () => {
  it('maps graph nodes and edges to DesignerStepDto connections', () => {
    const nodes: Node<AgentNodeData>[] = [
      {
        id: 'think-1',
        type: 'studioNode',
        position: { x: 0, y: 0 },
        data: { label: 'Think', type: 'think', description: 'reason' },
      },
      {
        id: 'act-1',
        type: 'studioNode',
        position: { x: 200, y: 0 },
        data: { label: 'Act', type: 'act', description: 'tool call' },
      }
    ];

    const edges: Edge[] = [{ id: 'think-1->act-1', source: 'think-1', target: 'act-1' }];

    const dto = mapGraphToDesignerDto({
      agentId: 'agent-1',
      graph: { nodes, edges },
      selectedNodeId: null,
      isDirty: true,
      isLoading: false,
      saveError: null,
      agentName: 'Demo',
      description: '',
      version: '1.0.0'
    });

    expect(dto.steps).toHaveLength(2);
    expect(dto.steps[0].connections).toEqual(['act-1']);
  });

  it('reports invalid variable references and nodes without exit', () => {
    const nodes: Node<AgentNodeData>[] = [
      {
        id: 'start',
        type: 'studioNode',
        position: { x: 0, y: 0 },
        data: { label: 'Start', type: 'think', description: 'Use {{missingVar}}' }
      }
    ];

    const issues = validateStudioGraph(nodes, []);
    expect(issues.some((issue) => issue.id.includes('invalid-var'))).toBe(true);
    expect(issues.some((issue) => issue.id.includes('no-exit'))).toBe(true);
  });

  it('builds guided simulation with variable context', () => {
    const nodes: Node<AgentNodeData>[] = [
      {
        id: 'start',
        type: 'studioNode',
        position: { x: 0, y: 0 },
        data: { label: 'Start', type: 'think', description: '', config: { outputs: ['intent'] } }
      },
      {
        id: 'final',
        type: 'studioNode',
        position: { x: 200, y: 0 },
        data: { label: 'Final', type: 'output', description: '' }
      }
    ];
    const edges: Edge[] = [{ id: 'start-final', source: 'start', target: 'final' }];

    const steps = buildSimulationSteps(nodes, edges);
    expect(steps).toHaveLength(2);
    expect(steps[0].variables.intent).toContain('value_1');
  });

  it('maps preview execution steps into inspectable timeline and sums tokens', () => {
    const timeline = [
      mapStepToTimeline({
        id: '1',
        stepType: 'think',
        iteration: 1,
        durationMs: 12,
        startedAt: new Date().toISOString(),
        tokensUsed: 30,
        isSuccess: true
      }),
      mapStepToTimeline({
        id: '2',
        stepType: 'act',
        iteration: 1,
        durationMs: 5,
        startedAt: new Date().toISOString(),
        tokensUsed: 10,
        inputJson: '{"tool":"search"}',
        outputJson: '{"ok":true}',
        isSuccess: true
      })
    ];

    expect(timeline[1].inputJson).toContain('search');
    expect(computeTotalTokens(timeline)).toBe(40);
  });
});
