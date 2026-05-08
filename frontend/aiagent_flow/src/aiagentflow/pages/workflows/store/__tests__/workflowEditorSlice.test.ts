 
import { it, expect, describe } from 'vitest';

import reducer, {
  createNewDraft,
  setDefinitionJson,
  selectWorkflowDraft,
} from '../workflowEditorSlice';

describe('workflowEditorSlice', () => {
  it('loads activities from definitionJson', () => {
    const next = reducer(
      undefined,
      setDefinitionJson(
        JSON.stringify({
          activities: [{ id: 'step_1', type: 'connect.send_whatsapp_template' }],
        })
      )
    );

    expect(next.activities).toHaveLength(1);
    expect(next.activities[0].id).toBe('step_1');
    expect(next.isDirty).toBe(true);
  });

  it('selects workflow and resets dirty flag', () => {
    const workflow = {
      id: 'wf_123',
      name: 'WF',
      triggerEventName: 'connect.message.received',
      version: 1,
      status: 'Draft',
      updatedAt: '',
      updatedBy: '',
      definitionJson: JSON.stringify({
        activities: [{ id: 'step_a', type: 'connect.update_inbox_status' }],
      }),
    };

    const next = reducer(undefined, selectWorkflowDraft(workflow));

    expect(next.selectedWorkflowId).toBe('wf_123');
    expect(next.draft.id).toBe('wf_123');
    expect(next.activities[0].id).toBe('step_a');
    expect(next.isDirty).toBe(false);
  });

  it('creates new draft', () => {
    const next = reducer(undefined, createNewDraft());
    expect(next.draft.id.startsWith('wf_')).toBe(true);
    expect(next.draft.name).toBe('New Workflow');
    expect(next.isDirty).toBe(true);
  });
});
