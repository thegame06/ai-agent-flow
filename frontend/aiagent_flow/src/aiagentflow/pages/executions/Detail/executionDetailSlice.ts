import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

export interface ExecutionStep {
  stepNumber: number;
  type: string;
  toolName?: string;
  inputJson?: string;
  outputJson?: string;
  rationale?: string;
  durationMs: number;
  status: string;
  createdAt: string;
}

export interface ExecutionDetail {
  id: string;
  agentDefinitionId: string;
  agentName: string;
  tenantId: string;
  status: string;
  input: { userMessage: string; contextJson?: string };
  output?: { finalResponse?: string; outputJson?: string };
  steps: ExecutionStep[];
  totalSteps: number;
  durationMs: number;
  createdAt: string;
  completedAt?: string;
  failureReason?: string;
  failureCode?: string;
  qualityScore?: number;
  hallucinationRisk?: string;
}

interface ExecutionDetailState {
  detail: ExecutionDetail | null;
  loading: boolean;
  error: string | null;
}

const initialState: ExecutionDetailState = {
  detail: null,
  loading: false,
  error: null,
};

export const fetchExecutionDetail = createAsyncThunk(
  'executionDetail/fetch',
  async ({ tenantId, executionId }: { tenantId: string; executionId: string }) => {
    const response = await axios.get(
      `/api/v1/tenants/${tenantId}/executions/${executionId}`
    );
    return response.data;
  }
);

const executionDetailSlice = createSlice({
  name: 'executionDetail',
  initialState,
  reducers: {
    clearDetail(state) {
      state.detail = null;
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchExecutionDetail.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchExecutionDetail.fulfilled, (state, action) => {
        state.loading = false;
        state.detail = action.payload;
      })
      .addCase(fetchExecutionDetail.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to load execution detail';
      });
  },
});

export const { clearDetail } = executionDetailSlice.actions;
export default executionDetailSlice.reducer;
