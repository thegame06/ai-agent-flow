import { useState, useEffect, useCallback } from 'react';

import { useAppDispatch, useAppSelector } from 'src/aiagentflow/store/hooks';

import {
  clearError,
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
  ThreadMessage} from '../Redux/Slice';

// ----------------------------------------------------------------------

interface UseThreadsReturn {
  // State
  threads: Thread[];
  currentThread: Thread | null;
  messages: ThreadMessage[];
  loading: boolean;
  error: string | null;
  total: number;

  // Actions
  loadThreads: (agentId?: string, status?: string, limit?: number) => Promise<void>;
  loadThreadDetail: (threadId: string) => Promise<void>;
  loadThreadHistory: (threadId: string, limit?: number) => Promise<void>;
  sendMessageToThread: (threadId: string, message: string) => Promise<void>;
  archiveThreadById: (threadId: string) => Promise<void>;
  deleteThreadById: (threadId: string) => Promise<void>;
  clearThreadState: () => void;
}

// ----------------------------------------------------------------------

export function useThreads(tenantId: string): UseThreadsReturn {
  const dispatch = useAppDispatch();
  const { threads, currentThread, messages, loading, error, total } = useAppSelector(
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

  const clearThreadState = useCallback(() => {
    dispatch(clearCurrentThread());
    dispatch(clearError());
  }, [dispatch]);

  // Auto-load threads on mount
  useEffect(() => {
    if (!initialized && tenantId) {
      loadThreads(undefined, 'Active', 50);
      setInitialized(true);
    }
  }, [initialized, tenantId, loadThreads]);

  return {
    threads,
    currentThread,
    messages,
    loading,
    error,
    total,
    loadThreads,
    loadThreadDetail,
    loadThreadHistory,
    sendMessageToThread,
    archiveThreadById,
    deleteThreadById,
    clearThreadState,
  };
}
