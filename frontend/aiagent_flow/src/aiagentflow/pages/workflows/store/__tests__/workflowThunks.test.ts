 
import { it, vi, expect, describe, beforeEach } from 'vitest';

import { workflowStudioApi } from '../../services/workflowStudioApi';
import {
  saveWorkflowDraft,
  fetchWorkflowRuntimeData,
} from '../workflowThunks';

vi.mock('../../services/workflowStudioApi', () => ({
  workflowStudioApi: {
    getDefinitions: vi.fn(),
    getExecutions: vi.fn(),
    getMetrics: vi.fn(),
    getAuditEvents: vi.fn(),
    getCatalogActivities: vi.fn(),
    getModels: vi.fn(),
    getTools: vi.fn(),
    upsertDefinition: vi.fn(),
  },
}));

describe('workflowThunks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetchWorkflowRuntimeData aggregates API payloads', async () => {
    vi.mocked(workflowStudioApi.getDefinitions).mockResolvedValue({ data: [{ id: 'wf_1' }] } as any);
    vi.mocked(workflowStudioApi.getExecutions).mockResolvedValue({ data: [{ id: 'ex_1' }] } as any);
    vi.mocked(workflowStudioApi.getMetrics).mockResolvedValue({ data: { total: 1 } } as any);
    vi.mocked(workflowStudioApi.getAuditEvents).mockResolvedValue({ data: [{ id: 'a_1' }] } as any);
    vi.mocked(workflowStudioApi.getCatalogActivities).mockResolvedValue({ data: [{ typeName: 'x' }] } as any);
    vi.mocked(workflowStudioApi.getModels).mockResolvedValue({ data: [{ modelId: 'gpt-4o' }] } as any);
    vi.mocked(workflowStudioApi.getTools).mockResolvedValue({ data: [{ key: 'http.request' }] } as any);

    const dispatch = vi.fn();
    const getState = vi.fn();
    const thunk = fetchWorkflowRuntimeData('tenant-1');
    const result = (await thunk(dispatch, getState, undefined)) as any;

    expect(result.type).toBe('workflowRuntime/fetchRuntimeData/fulfilled');
    expect(result.payload.workflows).toHaveLength(1);
    expect(result.payload.executions).toHaveLength(1);
    expect(result.payload.availableModels).toHaveLength(1);
    expect(result.payload.availableTools).toHaveLength(1);
  });

  it('saveWorkflowDraft rejects invalid JSON', async () => {
    const dispatch = vi.fn();
    const getState = vi.fn();
    const thunk = saveWorkflowDraft({
      tenantId: 'tenant-1',
      workflow: {
        id: 'wf_1',
        name: 'WF',
        triggerEventName: 'connect.message.received',
        definitionJson: '{invalid json}',
      },
    });
    const result = (await thunk(dispatch, getState, undefined)) as any;

    expect(result.type).toBe('workflowRuntime/saveWorkflowDraft/rejected');
    expect(workflowStudioApi.upsertDefinition).not.toHaveBeenCalled();
  });
});
