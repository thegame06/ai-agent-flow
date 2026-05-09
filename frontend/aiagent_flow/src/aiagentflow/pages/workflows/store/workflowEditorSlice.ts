import type { PayloadAction } from '@reduxjs/toolkit';

import { createSlice } from '@reduxjs/toolkit';

import { DEFAULT_DEFINITION } from '../constants';

import type { WorkflowEditorDraft, WorkflowEditorState } from './types';
import type { WorkflowDefinition, WorkflowActivityNode } from '../types';

const initialDraft: WorkflowEditorDraft = {
  id: '',
  name: '',
  triggerEventName: 'connect.message.received',
  definitionJson: DEFAULT_DEFINITION,
};

const initialState: WorkflowEditorState = {
  draft: initialDraft,
  activities: [],
  selectedWorkflowId: null,
  isDirty: false,
};

function parseActivitiesFromJson(definitionJson: string): WorkflowActivityNode[] {
  try {
    const parsed = JSON.parse(definitionJson) as { activities?: WorkflowActivityNode[] };
    return parsed.activities ?? [];
  } catch {
    return [];
  }
}

function syncDefinitionJsonFromActivities(
  definitionJson: string,
  activities: WorkflowActivityNode[]
): string {
  try {
    const parsed = JSON.parse(definitionJson) as Record<string, any>;
    parsed.activities = activities;
    return JSON.stringify(parsed, null, 2);
  } catch {
    return JSON.stringify({ activities }, null, 2);
  }
}

const workflowEditorSlice = createSlice({
  name: 'workflowEditor',
  initialState,
  reducers: {
    setSelectedWorkflowId(state, action: PayloadAction<string | null>) {
      state.selectedWorkflowId = action.payload;
    },
    setEditorField(
      state,
      action: PayloadAction<{ field: keyof WorkflowEditorDraft; value: string }>
    ) {
      const { field, value } = action.payload;
      state.draft[field] = value;
      state.isDirty = true;
    },
    setDefinitionJson(state, action: PayloadAction<string>) {
      state.draft.definitionJson = action.payload;
      state.activities = parseActivitiesFromJson(action.payload);
      state.isDirty = true;
    },
    selectWorkflowDraft(state, action: PayloadAction<WorkflowDefinition>) {
      const wf = action.payload;
      state.selectedWorkflowId = wf.id;
      state.draft = {
        id: wf.id,
        name: wf.name,
        triggerEventName: wf.triggerEventName,
        definitionJson: wf.definitionJson,
      };
      state.activities = parseActivitiesFromJson(wf.definitionJson);
      state.isDirty = false;
    },
    createNewDraft(state) {
      state.selectedWorkflowId = null;
      state.draft = {
        id: `wf_${Date.now()}`,
        name: 'New Workflow',
        triggerEventName: 'connect.message.received',
        definitionJson: DEFAULT_DEFINITION,
      };
      state.activities = parseActivitiesFromJson(DEFAULT_DEFINITION);
      state.isDirty = true;
    },
    addActivity(state, action: PayloadAction<WorkflowActivityNode>) {
      state.activities.push(action.payload);
      state.draft.definitionJson = syncDefinitionJsonFromActivities(
        state.draft.definitionJson,
        state.activities
      );
      state.isDirty = true;
    },
    updateActivity(
      state,
      action: PayloadAction<{ index: number; patch: Partial<WorkflowActivityNode> }>
    ) {
      const { index, patch } = action.payload;
      state.activities = state.activities.map((a, i) =>
        i === index ? { ...a, ...patch } : a
      );
      state.draft.definitionJson = syncDefinitionJsonFromActivities(
        state.draft.definitionJson,
        state.activities
      );
      state.isDirty = true;
    },
    setActivities(state, action: PayloadAction<WorkflowActivityNode[]>) {
      state.activities = action.payload;
      state.draft.definitionJson = syncDefinitionJsonFromActivities(
        state.draft.definitionJson,
        state.activities
      );
      state.isDirty = true;
    },
    markSaved(state) {
      state.isDirty = false;
    },
  },
});

export const {
  setSelectedWorkflowId,
  setEditorField,
  setDefinitionJson,
  selectWorkflowDraft,
  createNewDraft,
  addActivity,
  updateActivity,
  setActivities,
  markSaved,
} = workflowEditorSlice.actions;

export default workflowEditorSlice.reducer;
