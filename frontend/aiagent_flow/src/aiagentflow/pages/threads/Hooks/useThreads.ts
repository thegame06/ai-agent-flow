import { useState, useEffect, useCallback } from 'react';

import { useAppDispatch, useAppSelector } from 'src/aiagentflow/store/hooks';

import {
  clearError,
  updateThreadInbox,
  fetchThreadMetrics,
  sendMessage,
  fetchThreads,
  deleteThread,
  archiveThread,
  fetchThreadDetail,
  fetchThreadHistory,
  clearCurrentThread
} from '../Redux/Slice';

import type {
  Thread,
  ThreadMessage,
  InboxMetrics,
  UpdateThreadInboxPayload} from '../Redux/Slice';

// ----------------------------------------------------------------------

interface UseThreadsReturn {
  // State
  threads: Thread[];
  currentThread: Thread | null;
  messages: ThreadMessage[];
  metrics: InboxMetrics | null;
  loading: boolean;
  error: string | null;
  total: number;

  // Actions
  loadThreads: (agentId?: string, status?: string, limit?: number) => Promise<void>;
  loadThreadDetail: (threadId: string) => Promise<void>;
  loadThreadHistory: (threadId: string, limit?: number) => Promise<void>;
  loadThreadMetrics: (agentId?: string) => Promise<void>;
  sendMessageToThread: (threadId: string, message: string) => Promise<void>;
  archiveThreadById: (threadId: string) => Promise<void>;
  updateThreadInboxById: (payload: Omit<UpdateThreadInboxPayload, 'tenantId'>) => Promise<void>;
  deleteThreadById: (threadId: string) => Promise<void>;
  clearThreadState: () => void;
}

// ----------------------------------------------------------------------

export function useThreads(tenantId: string): UseThreadsReturn {
  const dispatch = useAppDispatch();
  const { threads, currentThread, messages, metrics, loading, error, total } = useAppSelector(
    (state) => state.threads
  );

  const [initialized, setInitialized] = useState(false);

  const loadThreads = useCallback(
    async (agentId?: string, status?: string, limit?: number) => {
      await dispatch(fetchThreads({ tenantId, agentId, status, limit })).unwrap();
    },
    [dispatch, tenantId]
  );

  const loadThreadDetail = useCallback(
    async (threadId: string) => {
      await dispatch(fetchThreadDetail({ tenantId, threadId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const loadThreadHistory = useCallback(
    async (threadId: string, limit?: number) => {
      await dispatch(fetchThreadHistory({ tenantId, threadId, limit })).unwrap();
    },
    [dispatch, tenantId]
  );

  const sendMessageToThread = useCallback(
    async (threadId: string, message: string) => {
      await dispatch(sendMessage({ tenantId, threadId, message })).unwrap();
    },
    [dispatch, tenantId]
  );

  const archiveThreadById = useCallback(
    async (threadId: string) => {
      await dispatch(archiveThread({ tenantId, threadId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const deleteThreadById = useCallback(
    async (threadId: string) => {
      await dispatch(deleteThread({ tenantId, threadId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const loadThreadMetrics = useCallback(
    async (agentId?: string) => {
      await dispatch(fetchThreadMetrics({ tenantId, agentId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const updateThreadInboxById = useCallback(
    async (payload: Omit<UpdateThreadInboxPayload, 'tenantId'>) => {
      await dispatch(updateThreadInbox({ ...payload, tenantId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const clearThreadState = useCallback(() => {
    dispatch(clearCurrentThread());
    dispatch(clearError());
  }, [dispatch]);

  // Auto-load threads on mount
  useEffect(() => {
    if (!initialized && tenantId) {
      loadThreads(undefined, 'Active', 50);
      loadThreadMetrics();
      setInitialized(true);
    }
  }, [initialized, tenantId, loadThreads, loadThreadMetrics]);

  return {
    threads,
    currentThread,
    messages,
    metrics,
    loading,
    error,
    total,
    loadThreads,
    loadThreadDetail,
    loadThreadHistory,
    loadThreadMetrics,
    sendMessageToThread,
    archiveThreadById,
    updateThreadInboxById,
    deleteThreadById,
    clearThreadState,
  };
}
