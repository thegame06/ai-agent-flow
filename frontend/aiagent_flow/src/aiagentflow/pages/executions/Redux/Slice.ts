import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

export const fetchExecutions = createAsyncThunk('executions/fetchAll', async (tenantId: string) => {
  const response = await axios.get(`/api/v1/tenants/${tenantId}/executions`);
  return response.data;
});

const executionsSlice = createSlice({
  name: 'executions',
  initialState: {
    items: [],
    loading: false,
    error: null as string | null | undefined,
  },
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchExecutions.pending, (state) => {
        state.loading = true;
      })
      .addCase(fetchExecutions.fulfilled, (state, action) => {
        state.loading = false;
        state.items = action.payload;
      })
      .addCase(fetchExecutions.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });
  },
});

export default executionsSlice.reducer;
