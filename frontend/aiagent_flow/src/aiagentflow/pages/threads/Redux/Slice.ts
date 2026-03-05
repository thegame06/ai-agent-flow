import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios, { endpoints } from 'src/lib/axios';

// ----------------------------------------------------------------------

export interface Thread {
  id: string;
  threadKey: string;
  agentId: string;
  agentName?: string;
  userId: string;
  status: 'Active' | 'Archived' | 'Expired' | 'Completed';
  turnCount: number;
  maxTurns: number;
  createdAt: string;
  expiresAt?: string;
  lastActivityAt?: string;
  metadata?: Record<string, any>;
}

export interface ThreadMessage {
  id: string;
  threadId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  createdAt: string;
  executionId?: string;
}

export interface ThreadsState {
  threads: Thread[];
  currentThread: Thread | null;
  messages: ThreadMessage[];
  loading: boolean;
  error: string | null;
  total: number;
}

// ----------------------------------------------------------------------

const initialState: ThreadsState = {
  threads: [],
  currentThread: null,
  messages: [],
  loading: false,
  error: null,
  total: 0,
};

// ----------------------------------------------------------------------

export const fetchThreads = createAsyncThunk(
  'threads/fetchThreads',
  async ({ tenantId, agentId, status, limit }: { tenantId: string; agentId?: string; status?: string; limit?: number }) => {
    const params = new URLSearchParams();
    if (agentId) params.append('agentId', agentId);
    if (status) params.append('status', status);
    if (limit) params.append('limit', limit.toString());

    const response = await axios.get(`${endpoints.agentflow.threads.list(tenantId)}?${params.toString()}`);
    return response.data as Thread[];
  }
);

export const fetchThreadDetail = createAsyncThunk(
  'threads/fetchThreadDetail',
  async ({ tenantId, threadId }: { tenantId: string; threadId: string }) => {
    const response = await axios.get(endpoints.agentflow.threads.detail(tenantId, threadId));
    return response.data as Thread;
  }
);

export const fetchThreadHistory = createAsyncThunk(
  'threads/fetchThreadHistory',
  async ({ tenantId, threadId, limit }: { tenantId: string; threadId: string; limit?: number }) => {
    const params = limit ? `?limit=${limit}` : '';
    const response = await axios.get(`${endpoints.agentflow.threads.history(tenantId, threadId)}${params}`);
    return response.data as ThreadMessage[];
  }
);

export const sendMessage = createAsyncThunk(
  'threads/sendMessage',
  async ({ tenantId, threadId, message }: { tenantId: string; threadId: string; message: string }) => {
    const response = await axios.post(endpoints.agentflow.threads.sendMessage(tenantId, threadId), {
      role: 'user',
      content: message,
    });
    return response.data as ThreadMessage;
  }
);

export const archiveThread = createAsyncThunk(
  'threads/archiveThread',
  async ({ tenantId, threadId }: { tenantId: string; threadId: string }) => {
    await axios.post(endpoints.agentflow.threads.archive(tenantId, threadId));
    return threadId;
  }
);

export const deleteThread = createAsyncThunk(
  'threads/deleteThread',
  async ({ tenantId, threadId }: { tenantId: string; threadId: string }) => {
    await axios.delete(endpoints.agentflow.threads.delete(tenantId, threadId));
    return threadId;
  }
);

// ----------------------------------------------------------------------

const slice = createSlice({
  name: 'threads',
  initialState,
  reducers: {
    clearError(state) {
      state.error = null;
    },
    clearCurrentThread(state) {
      state.currentThread = null;
      state.messages = [];
    },
  },
  extraReducers: (builder) => {
    builder
      // Fetch threads
      .addCase(fetchThreads.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchThreads.fulfilled, (state, action) => {
        state.loading = false;
        state.threads = action.payload;
        state.total = action.payload.length;
      })
      .addCase(fetchThreads.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch threads';
      })
      // Fetch thread detail
      .addCase(fetchThreadDetail.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchThreadDetail.fulfilled, (state, action) => {
        state.loading = false;
        state.currentThread = action.payload;
      })
      .addCase(fetchThreadDetail.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch thread detail';
      })
      // Fetch thread history
      .addCase(fetchThreadHistory.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchThreadHistory.fulfilled, (state, action) => {
        state.loading = false;
        state.messages = action.payload;
      })
      .addCase(fetchThreadHistory.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to fetch thread history';
      })
      // Send message
      .addCase(sendMessage.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(sendMessage.fulfilled, (state, action) => {
        state.loading = false;
        state.messages.push(action.payload);
      })
      .addCase(sendMessage.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to send message';
      })
      // Archive thread
      .addCase(archiveThread.fulfilled, (state, action) => {
        const thread = state.threads.find((t) => t.id === action.payload);
        if (thread) {
          thread.status = 'Archived';
        }
      })
      // Delete thread
      .addCase(deleteThread.fulfilled, (state, action) => {
        state.threads = state.threads.filter((t) => t.id !== action.payload);
        if (state.currentThread?.id === action.payload) {
          state.currentThread = null;
          state.messages = [];
        }
      });
  },
});

export const { clearError, clearCurrentThread } = slice.actions;
export default slice.reducer;
