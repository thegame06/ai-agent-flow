import type { PayloadAction } from '@reduxjs/toolkit';

import { createSlice } from '@reduxjs/toolkit';

import { DEFAULT_AGENT_DRAFT } from './types';
import { saveAgent, publishAgent, fetchAgentDetail, mapResponseToDraft } from './designerThunks';

import type { AgentStep, AgentToolBinding, AgentDefinitionDraft } from './types';

interface DesignerState {
  draft: AgentDefinitionDraft;
  activeTab: number;
  isDirty: boolean;
  saving: boolean;
  errors: Record<string, string>;
}

const initialState: DesignerState = {
  draft: DEFAULT_AGENT_DRAFT,
  activeTab: 0,
  isDirty: false,
  saving: false,
  errors: {},
};

const designerSlice = createSlice({
  name: 'designer',
  initialState,
  reducers: {
    // ── Tab Navigation ──
    setActiveTab(state, action: PayloadAction<number>) {
      state.activeTab = action.payload;
    },

    // ── General Info ──
    updateField(state, action: PayloadAction<{ field: keyof AgentDefinitionDraft; value: unknown }>) {
      const { field, value } = action.payload;
      (state.draft as Record<string, unknown>)[field] = value;
      state.isDirty = true;
    },

    // ── Steps ──
    addStep(state, action: PayloadAction<AgentStep>) {
      state.draft.steps.push(action.payload);
      state.isDirty = true;
    },
    removeStep(state, action: PayloadAction<string>) {
      state.draft.steps = state.draft.steps.filter((s) => s.id !== action.payload);
      state.draft.steps.forEach((s) => {
        s.connections = s.connections.filter((c) => c !== action.payload);
      });
      state.isDirty = true;
    },
    updateStep(state, action: PayloadAction<{ id: string; changes: Partial<AgentStep> }>) {
      const idx = state.draft.steps.findIndex((s) => s.id === action.payload.id);
      if (idx >= 0) {
        state.draft.steps[idx] = { ...state.draft.steps[idx], ...action.payload.changes };
        state.isDirty = true;
      }
    },

    // ── Tools ──
    addTool(state, action: PayloadAction<AgentToolBinding>) {
      state.draft.tools.push(action.payload);
      state.isDirty = true;
    },
    removeTool(state, action: PayloadAction<string>) {
      state.draft.tools = state.draft.tools.filter((t) => t.toolId !== action.payload);
      state.isDirty = true;
    },

    // ── Guardrails ──
    updateGuardrails(
      state,
      action: PayloadAction<Partial<AgentDefinitionDraft['guardrails']>>
    ) {
      state.draft.guardrails = { ...state.draft.guardrails, ...action.payload };
      state.isDirty = true;
    },

    // ── Memory ──
    updateMemory(state, action: PayloadAction<Partial<AgentDefinitionDraft['memory']>>) {
      state.draft.memory = { ...state.draft.memory, ...action.payload };
      state.isDirty = true;
    },

    // ── Model Config ──
    updateModel(state, action: PayloadAction<Partial<AgentDefinitionDraft['model']>>) {
      state.draft.model = { ...state.draft.model, ...action.payload };
      state.isDirty = true;
    },

    // ── Load / Reset ──
    loadDraft(state, action: PayloadAction<AgentDefinitionDraft>) {
      state.draft = action.payload;
      state.isDirty = false;
    },
    resetDraft(state) {
      state.draft = DEFAULT_AGENT_DRAFT;
      state.isDirty = false;
      state.activeTab = 0;
    },
  },
  extraReducers: (builder) => {
    // ── Fetch agent detail for editing ──
    builder.addCase(fetchAgentDetail.pending, (state) => {
      state.saving = true;
      state.errors = {};
    });
    builder.addCase(fetchAgentDetail.fulfilled, (state, action) => {
      state.draft = mapResponseToDraft(action.payload);
      state.isDirty = false;
      state.saving = false;
    });
    builder.addCase(fetchAgentDetail.rejected, (state, action) => {
      state.saving = false;
      state.errors = { load: action.error.message ?? 'Failed to load agent' };
    });

    // ── Save agent ──
    builder.addCase(saveAgent.pending, (state) => {
      state.saving = true;
      state.errors = {};
    });
    builder.addCase(saveAgent.fulfilled, (state, action) => {
      state.draft = mapResponseToDraft(action.payload);
      state.isDirty = false;
      state.saving = false;
    });
    builder.addCase(saveAgent.rejected, (state, action) => {
      state.saving = false;
      state.errors = { save: action.error.message ?? 'Failed to save agent' };
    });

    // ── Publish agent ──
    builder.addCase(publishAgent.fulfilled, (state) => {
      state.draft.status = 'Published';
      state.isDirty = false;
    });
  },
});

export const {
  setActiveTab,
  updateField,
  addStep,
  removeStep,
  updateStep,
  addTool,
  removeTool,
  updateGuardrails,
  updateMemory,
  updateModel,
  loadDraft,
  resetDraft,
} = designerSlice.actions;

export default designerSlice.reducer;
