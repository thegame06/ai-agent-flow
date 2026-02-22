import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

export const fetchTools = createAsyncThunk('tools/fetchAll', async () => {
  const response = await axios.get('/api/v1/extensions/tools');
  return response.data;
});

const toolsSlice = createSlice({
  name: 'tools',
  initialState: {
    items: [],
    loading: false,
    error: null as string | null | undefined,
  },
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchTools.pending, (state) => {
        state.loading = true;
      })
      .addCase(fetchTools.fulfilled, (state, action) => {
        state.loading = false;
        state.items = action.payload;
      })
      .addCase(fetchTools.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message;
      });
  },
});

export default toolsSlice.reducer;
