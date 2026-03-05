import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios, { endpoints } from 'src/lib/axios';

// ----------------------------------------------------------------------

export interface Evaluation {
  id: string;
  runId: string;
  agentId: string;
  agentName?: string;
  executionId: string;
  status: 'Pending' | 'Completed' | 'Failed';
  overallScore?: number;
  metrics?: Record<string, number>;
  createdAt: string;
  completedAt?: string;
  reviewerId?: string;
  notes?: string;
}

export interface EvaluationSummary {
  agentId: string;
  agentName: string;
  totalEvaluations: number;
  averageScore: number;
  passRate: number;
  championScore?: number;
  challengerScore?: number;
  trend: 'up' | 'down' | 'stable';
}

export interface EvaluationsState {
  evaluations: Evaluation[];
  summaries: EvaluationSummary[];
  pendingReview: Evaluation[];
  loading: boolean;
  error: string | null;
}

// ----------------------------------------------------------------------

const initialState: EvaluationsState = {
  evaluations: [],
  summaries: [],
  pendingReview: [],
  loading: false,
  error: null,
};

// ----------------------------------------------------------------------

export const fetchEvaluations = createAsyncThunk(
  'evaluations/fetchEvaluations',
  async ({ tenantId, agentId, limit }: { tenantId: string; agentId?: string; limit?: number }) => {
    let url = endpoints.agentflow.evaluations.list(tenantId);
    if (agentId) {
      url = endpoints.agentflow.evaluations.byAgent(tenantId, agentId);
    }
    if (limit) {
      url += `${url.includes('?') ? '&' : '?'}limit=${limit}`;
    }

    const response = await axios.get(url);
    return response.data as Evaluation[];
  }
);

export const fetchEvaluationSummary = createAsyncThunk(
  'evaluations/fetchEvaluationSummary',
  async ({ tenantId, agentId }: { tenantId: string; agentId: string }) => {
    const response = await axios.get(endpoints.agentflow.evaluations.agentSummary(tenantId, agentId));
    return response.data as EvaluationSummary;
  }
);

export const fetchPendingReview = createAsyncThunk(
  'evaluations/fetchPendingReview',
  async ({ tenantId }: { tenantId: string }) => {
    const response = await axios.get(endpoints.agentflow.evaluations.pendingReview(tenantId));
    return response.data as Evaluation[];
  }
);

export const fetchEvaluationByExecution = createAsyncThunk(
  'evaluations/fetchEvaluationByExecution',
  async ({ tenantId, executionId }: { tenantId: string; executionId: string }) => {
    const response = await axios.get(endpoints.agentflow.evaluations.byExecution(tenantId, executionId));
    return response.data as Evaluation;
  }
);

// ----------------------------------------------------------------------

const slice = createSlice({
  name: 'evaluations',
  initialState,
  reducers: {
    clearError(state) {
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      // Fetch evaluations
      .addCase(fetchEvaluations.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchEvaluations.fulfilled, (state, action) => {
        state.loading = false;
        state.evaluations = action.payload;
      })
      .addCase(fetchEvaluations.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch evaluations';
      })
      // Fetch summary
      .addCase(fetchEvaluationSummary.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchEvaluationSummary.fulfilled, (state, action) => {
        state.loading = false;
        state.summaries.push(action.payload);
      })
      .addCase(fetchEvaluationSummary.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch evaluation summary';
      })
      // Fetch pending review
      .addCase(fetchPendingReview.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchPendingReview.fulfilled, (state, action) => {
        state.loading = false;
        state.pendingReview = action.payload;
      })
      .addCase(fetchPendingReview.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch pending reviews';
      })
      // Fetch by execution
      .addCase(fetchEvaluationByExecution.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchEvaluationByExecution.fulfilled, (state, action) => {
        state.loading = false;
        const index = state.evaluations.findIndex((e) => e.id === action.payload.id);
        if (index >= 0) {
          state.evaluations[index] = action.payload;
        } else {
          state.evaluations.push(action.payload);
        }
      })
      .addCase(fetchEvaluationByExecution.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch evaluation';
      });
  },
});

export const { clearError } = slice.actions;
export default slice.reducer;
