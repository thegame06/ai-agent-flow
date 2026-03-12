import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios, { endpoints } from 'src/lib/axios';

// ----------------------------------------------------------------------

export interface Thread {
  id: string;
  threadKey: string;
  agentId: string;
  agentName?: string;
  userId?: string;
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

const normalizeThread = (raw: any): Thread => ({
  id: raw.id ?? raw.threadId,
  threadKey: raw.threadKey,
  agentId: raw.agentId,
  agentName: raw.agentName,
  userId: raw.userId,
  status: raw.status,
  turnCount: raw.turnCount ?? 0,
  maxTurns: raw.maxTurns ?? 0,
  createdAt: raw.createdAt,
  expiresAt: raw.expiresAt,
  lastActivityAt: raw.lastActivityAt,
  metadata: raw.metadata,
});

export const fetchThreads = createAsyncThunk(
  'threads/fetchThreads',
  async ({ tenantId, agentId, status, limit }: { tenantId: string; agentId?: string; status?: string; limit?: number }) => {
    const params = new URLSearchParams();
    if (agentId) params.append('agentId', agentId);
    // Backend currently supports agentId filter only for list endpoint.
    if (limit) params.append('limit', limit.toString());

    const query = params.toString();
    const response = await axios.get(
      `${endpoints.agentflow.threads.list(tenantId)}${query ? `?${query}` : ''}`
    );

    const normalized = (response.data ?? []).map(normalizeThread);
    return status ? normalized.filter((t: Thread) => t.status === status) : normalized;
  }
);

export const fetchThreadDetail = createAsyncThunk(
  'threads/fetchThreadDetail',
  async ({ tenantId, threadId }: { tenantId: string; threadId: string }) => {
    const response = await axios.get(endpoints.agentflow.threads.detail(tenantId, threadId));
    return normalizeThread(response.data);
  }
);

export const fetchThreadHistory = createAsyncThunk(
  'threads/fetchThreadHistory',
  async ({ tenantId, threadId, limit }: { tenantId: string; threadId: string; limit?: number }) => {
    const params = limit ? `?maxTurns=${limit}` : '';
    const response = await axios.get(`${endpoints.agentflow.threads.history(tenantId, threadId)}${params}`);

    const turns = response.data?.turns ?? [];
    const messages: ThreadMessage[] = turns.flatMap((turn: any, idx: number) => {
      const ts = turn.timestamp ?? new Date().toISOString();
      const arr: ThreadMessage[] = [
        {
          id: `${threadId}-u-${idx}`,
          threadId,
          role: 'user',
          content: turn.userMessage ?? '',
          createdAt: ts,
        },
      ];

      if (turn.assistantResponse) {
        arr.push({
          id: `${threadId}-a-${idx}`,
          threadId,
          role: 'assistant',
          content: turn.assistantResponse,
          createdAt: ts,
        });
      }

      return arr;
    });

    return messages;
  }
);

export const sendMessage = createAsyncThunk(
  'threads/sendMessage',
  async ({ tenantId, threadId, message }: { tenantId: string; threadId: string; message: string }) => {
    const response = await axios.post(endpoints.agentflow.threads.sendMessage(tenantId, threadId), {
      message,
    });

    return {
      id: response.data?.executionId ?? `${threadId}-${Date.now()}`,
      threadId,
      role: 'assistant',
      content: response.data?.assistantResponse ?? '',
      createdAt: new Date().toISOString(),
      executionId: response.data?.executionId,
    } as ThreadMessage;
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
