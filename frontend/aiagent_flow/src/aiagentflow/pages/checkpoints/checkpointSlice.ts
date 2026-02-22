import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

export interface Checkpoint {
  executionId: string;
  tenantId: string;
  agentKey: string;
  checkpointId: string;
  reason: string;
  toolName?: string;
  toolInputJson?: string;
  llmRationale?: string;
  createdAt: string;
  context: Record<string, string>;
}

interface CheckpointState {
  items: Checkpoint[];
  loading: boolean;
  decidingId: string | null;
  error: string | null;
}

const initialState: CheckpointState = {
  items: [],
  loading: false,
  decidingId: null,
  error: null,
};

export const fetchCheckpoints = createAsyncThunk(
  'checkpoints/fetchAll',
  async (tenantId: string) => {
    const response = await axios.get(`/api/v1/tenants/${tenantId}/checkpoints`);
    return response.data;
  }
);

export const decideCheckpoint = createAsyncThunk(
  'checkpoints/decide',
  async ({ tenantId, executionId, approved, feedback }: {
    tenantId: string;
    executionId: string;
    approved: boolean;
    feedback?: string;
  }) => {
    const response = await axios.post(
      `/api/v1/tenants/${tenantId}/checkpoints/${executionId}/decide`,
      { approved, feedback }
    );
    return { executionId, response: response.data };
  }
);

const checkpointSlice = createSlice({
  name: 'checkpoints',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchCheckpoints.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchCheckpoints.fulfilled, (state, action) => {
        state.loading = false;
        state.items = action.payload;
      })
      .addCase(fetchCheckpoints.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to load checkpoints';
      })
      .addCase(decideCheckpoint.pending, (state, action) => {
        state.decidingId = action.meta.arg.executionId;
      })
      .addCase(decideCheckpoint.fulfilled, (state, action) => {
        state.decidingId = null;
        state.items = state.items.filter((c) => c.executionId !== action.payload.executionId);
      })
      .addCase(decideCheckpoint.rejected, (state) => {
        state.decidingId = null;
      });
  },
});

export default checkpointSlice.reducer;
