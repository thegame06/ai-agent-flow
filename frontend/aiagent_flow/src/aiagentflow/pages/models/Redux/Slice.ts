import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

// ── Types ──

export interface ModelProvider {
  modelId: string;
  providerId: string;
  displayName: string;
  costPer1KTokens: number;
  maxContextTokens: number;
  tier: string;
  status: string;
}

interface ModelsState {
  items: ModelProvider[];
  healthyModels: string[];
  loading: boolean;
  error: string | null;
}

// ── Initial State ──

const initialState: ModelsState = {
  items: [],
  healthyModels: [],
  loading: false,
  error: null,
};

// ── Thunks ──

export const fetchModels = createAsyncThunk('models/fetchAll', async () => {
  const response = await axios.get('/api/v1/model-routing/models');
  return response.data;
});

export const fetchHealthyModels = createAsyncThunk('models/fetchHealthy', async () => {
  const response = await axios.get('/api/v1/model-routing/models/healthy');
  return response.data;
});

// ── Slice ──

const modelsSlice = createSlice({
  name: 'models',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    // Fetch all models
    builder
      .addCase(fetchModels.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchModels.fulfilled, (state, action) => {
        state.loading = false;
        state.items = action.payload;
      })
      .addCase(fetchModels.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to load models';
      });

    // Fetch healthy models
    builder
      .addCase(fetchHealthyModels.pending, (state) => {
        state.loading = true;
      })
      .addCase(fetchHealthyModels.fulfilled, (state, action) => {
        state.loading = false;
        state.healthyModels = action.payload;
      })
      .addCase(fetchHealthyModels.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to load healthy models';
      });
  },
});

export default modelsSlice.reducer;
