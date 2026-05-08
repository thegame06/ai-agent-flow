 
import { it, expect, describe } from 'vitest';

import reducer, {
  setWorkflowStepsOpen,
  setWorkflowRuntimeError,
} from '../workflowRuntimeSlice';
import {
  runWorkflowEvent,
  fetchWorkflowRuntimeData,
  fetchWorkflowExecutionSteps,
} from '../workflowThunks';

describe('workflowRuntimeSlice', () => {
  it('sets loading=true on fetchWorkflowRuntimeData.pending', () => {
    const next = reducer(
      undefined,
      fetchWorkflowRuntimeData.pending('req-1', 'tenant-1')
    );

    expect(next.loading).toBe(true);
    expect(next.error).toBeNull();
  });

  it('maps payload on fetchWorkflowRuntimeData.fulfilled', () => {
    const payload = {
      workflows: [{ id: 'wf_1' }],
      executions: [{ id: 'ex_1' }],
      metrics: { total: 10, successRate: 0.9, failureRate: 0.1, avgLatencyMs: 100, activityMetrics: [] },
      auditEvents: [{ id: 'a_1' }],
      activityCatalog: [{ typeName: 'connect.send_whatsapp_template' }],
      availableModels: [{ modelId: 'gpt-4o', displayName: 'GPT-4o' }],
      availableTools: [{ key: 'http.request', displayName: 'HTTP Request' }],
    };

    const next = reducer(
      undefined,
      fetchWorkflowRuntimeData.fulfilled(payload as any, 'req-2', 'tenant-1')
    );

    expect(next.loading).toBe(false);
    expect(next.workflows).toHaveLength(1);
    expect(next.executions).toHaveLength(1);
    expect(next.availableModels[0].modelId).toBe('gpt-4o');
    expect(next.availableTools[0].key).toBe('http.request');
  });

  it('sets running false and error on runWorkflowEvent.rejected', () => {
    const pendingState = reducer(
      undefined,
      runWorkflowEvent.pending('req-3', { tenantId: 'tenant-1', eventName: 'x' })
    );
    const next = reducer(
      pendingState,
      runWorkflowEvent.rejected(
        new Error('boom'),
        'req-3',
        { tenantId: 'tenant-1', eventName: 'x' }
      )
    );

    expect(next.running).toBe(false);
    expect(next.error).toContain('boom');
  });

  it('opens steps dialog and sets steps on fetchWorkflowExecutionSteps.fulfilled', () => {
    const steps = [{ id: 's1', activityType: 'a', activityName: 'A', status: 'Completed', startedAt: '' }];
    const next = reducer(
      undefined,
      fetchWorkflowExecutionSteps.fulfilled(
        steps as any,
        'req-4',
        { tenantId: 'tenant-1', executionId: 'ex-1' }
      )
    );

    expect(next.stepsOpen).toBe(true);
    expect(next.steps).toHaveLength(1);
  });

  it('handles local reducers', () => {
    const withError = reducer(undefined, setWorkflowRuntimeError('x'));
    expect(withError.error).toBe('x');
    const withDialog = reducer(withError, setWorkflowStepsOpen(true));
    expect(withDialog.stepsOpen).toBe(true);
  });
});
