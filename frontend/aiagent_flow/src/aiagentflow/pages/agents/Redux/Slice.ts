import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

// ── THUNKS (API calls) ──

export const fetchAgents = createAsyncThunk('agents/fetchAll', async (tenantId: string) => {
  const response = await axios.get(`/api/v1/tenants/${tenantId}/agents`);
  return response.data;
});

export const fetchAgentById = createAsyncThunk(
  'agents/fetchById',
  async ({ tenantId, agentId }: { tenantId: string; agentId: string }) => {
    const response = await axios.get(`/api/v1/tenants/${tenantId}/agents/${agentId}`);
    return response.data;
  }
);

export const createAgent = createAsyncThunk(
  'agents/create',
  async ({ tenantId, payload }: { tenantId: string; payload: any }) => {
    const response = await axios.post(`/api/v1/tenants/${tenantId}/agents`, payload);
    return response.data;
  }
);

export const updateAgent = createAsyncThunk(
  'agents/update',
  async ({ tenantId, agentId, payload }: { tenantId: string; agentId: string; payload: any }) => {
    const response = await axios.put(`/api/v1/tenants/${tenantId}/agents/${agentId}`, payload);
    return response.data;
  }
);

export const deleteAgent = createAsyncThunk(
  'agents/delete',
  async ({ tenantId, agentId }: { tenantId: string; agentId: string }) => {
    await axios.delete(`/api/v1/tenants/${tenantId}/agents/${agentId}`);
    return agentId; // Return ID for slice to remove from state
  }
);

export const cloneAgent = createAsyncThunk(
  'agents/clone',
  async ({ tenantId, agentId, newName, newDescription }: { 
    tenantId: string; 
    agentId: string; 
    newName: string; 
    newDescription?: string 
  }) => {
    const response = await axios.post(
      `/api/v1/tenants/${tenantId}/agents/${agentId}/clone`,
      { newName, newDescription }
    );
    return response.data;
  }
);

export const publishAgent = createAsyncThunk(
  'agents/publish',
  async ({ tenantId, agentId }: { tenantId: string; agentId: string }) => {
    const response = await axios.post(`/api/v1/tenants/${tenantId}/agents/${agentId}/publish`);
    return response.data;
  }
);

// ── SLICE ──

const agentsSlice = createSlice({
  name: 'agents',
  initialState: {
    items: [] as any[],
    selectedAgent: null as any,
    loading: false,
    error: null as string | null | undefined,
  },
  reducers: {
    clearSelectedAgent(state) {
      state.selectedAgent = null;
    },
  },
  extraReducers: (builder) => {
    // ── Fetch agents ──
    builder
      .addCase(fetchAgents.pending, (state) => {
        state.loading = true;
      })
      .addCase(fetchAgents.fulfilled, (state, action) => {
        state.loading = false;
        state.items = action.payload;
      })
      .addCase(fetchAgents.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });

    // ── Fetch agent by ID ──
    builder
      .addCase(fetchAgentById.pending, (state) => {
        state.loading = true;
      })
      .addCase(fetchAgentById.fulfilled, (state, action) => {
        state.loading = false;
        state.selectedAgent = action.payload;
      })
      .addCase(fetchAgentById.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });

    // ── Create agent ──
    builder
      .addCase(createAgent.pending, (state) => {
        state.loading = true;
      })
      .addCase(createAgent.fulfilled, (state, action) => {
        state.loading = false;
        state.items.push(action.payload);
      })
      .addCase(createAgent.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });

    // ── Update agent ──
    builder
      .addCase(updateAgent.pending, (state) => {
        state.loading = true;
      })
      .addCase(updateAgent.fulfilled, (state, action) => {
        state.loading = false;
        const index = state.items.findIndex((a) => a.id === action.payload.id);
        if (index !== -1) {
          state.items[index] = action.payload;
        }
        if (state.selectedAgent?.id === action.payload.id) {
          state.selectedAgent = action.payload;
        }
      })
      .addCase(updateAgent.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });

    // ── Delete agent ──
    builder
      .addCase(deleteAgent.pending, (state) => {
        state.loading = true;
      })
      .addCase(deleteAgent.fulfilled, (state, action) => {
        state.loading = false;
        state.items = state.items.filter((a) => a.id !== action.payload);
        if (state.selectedAgent?.id === action.payload) {
          state.selectedAgent = null;
        }
      })
      .addCase(deleteAgent.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });

    // ── Clone agent ──
    builder
      .addCase(cloneAgent.pending, (state) => {
        state.loading = true;
      })
      .addCase(cloneAgent.fulfilled, (state, action) => {
        state.loading = false;
        state.items.push(action.payload);
      })
      .addCase(cloneAgent.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });

    // ── Publish agent ──
    builder
      .addCase(publishAgent.pending, (state) => {
        state.loading = true;
      })
      .addCase(publishAgent.fulfilled, (state, action) => {
        state.loading = false;
        const index = state.items.findIndex((a) => a.id === action.payload.id);
        if (index !== -1) {
          state.items[index] = { ...state.items[index], status: 'Published' };
        }
        if (state.selectedAgent?.id === action.payload.id) {
          state.selectedAgent = { ...state.selectedAgent, status: 'Published' };
        }
      })
      .addCase(publishAgent.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });
  },
});

export const { clearSelectedAgent } = agentsSlice.actions;

export default agentsSlice.reducer;
