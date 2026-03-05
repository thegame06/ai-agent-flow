import { useEffect, useCallback } from 'react';

import { useAppDispatch, useAppSelector } from 'src/aiagentflow/store/hooks';

import {
  clearError,
  fetchEvaluations,
  fetchPendingReview,
  fetchEvaluationSummary,
  fetchEvaluationByExecution
} from '../Redux/Slice';

import type {
  Evaluation,
  EvaluationSummary} from '../Redux/Slice';

// ----------------------------------------------------------------------

interface UseEvaluationsReturn {
  // State
  evaluations: Evaluation[];
  summaries: EvaluationSummary[];
  pendingReview: Evaluation[];
  loading: boolean;
  error: string | null;

  // Actions
  loadEvaluations: (agentId?: string, limit?: number) => Promise<void>;
  loadSummary: (agentId: string) => Promise<void>;
  loadPendingReview: () => Promise<void>;
  loadByExecution: (executionId: string) => Promise<void>;
  clearEvaluationError: () => void;
}

// ----------------------------------------------------------------------

export function useEvaluations(tenantId: string): UseEvaluationsReturn {
  const dispatch = useAppDispatch();
  const { evaluations, summaries, pendingReview, loading, error } = useAppSelector(
    (state) => state.evaluations
  );

  const loadEvaluations = useCallback(
    async (agentId?: string, limit?: number) => {
      await dispatch(fetchEvaluations({ tenantId, agentId, limit })).unwrap();
    },
    [dispatch, tenantId]
  );

  const loadSummary = useCallback(
    async (agentId: string) => {
      await dispatch(fetchEvaluationSummary({ tenantId, agentId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const loadPendingReview = useCallback(async () => {
    await dispatch(fetchPendingReview({ tenantId })).unwrap();
  }, [dispatch, tenantId]);

  const loadByExecution = useCallback(
    async (executionId: string) => {
      await dispatch(fetchEvaluationByExecution({ tenantId, executionId })).unwrap();
    },
    [dispatch, tenantId]
  );

  const clearEvaluationError = useCallback(() => {
    dispatch(clearError());
  }, [dispatch]);

  // Auto-load evaluations and pending review on mount
  useEffect(() => {
    if (tenantId) {
      loadEvaluations(undefined, 50);
      loadPendingReview();
    }
  }, [tenantId, loadEvaluations, loadPendingReview]);

  return {
    evaluations,
    summaries,
    pendingReview,
    loading,
    error,
    loadEvaluations,
    loadSummary,
    loadPendingReview,
    loadByExecution,
    clearEvaluationError,
  };
}
