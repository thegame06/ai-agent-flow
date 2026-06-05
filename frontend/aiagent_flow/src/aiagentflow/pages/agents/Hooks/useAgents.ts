import type { RootState } from 'src/aiagentflow/store';

import { useEffect, useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import {
  cloneAgent,
  fetchAgents,
  fetchAgentStats,
  createAgent,
  updateAgent,
  deleteAgent,
  publishAgent,
  fetchAgentById,
  clearSelectedAgent,
} from '../Redux/Slice';

export function useAgents(tenantId: string, runtimeKind?: string | null) {
  const dispatch = useDispatch();
  const { items, stats, selectedAgent, loading, statsLoading, error } = useSelector((state: RootState) => state.agents);

  useEffect(() => {
    if (tenantId) {
      dispatch(fetchAgents({ tenantId, runtimeKind }) as any);
      dispatch(fetchAgentStats({ tenantId }) as any);
    }
  }, [dispatch, tenantId, runtimeKind]);

  // ── CRUD Operations ──

  const getAgentById = useCallback(
    (agentId: string) => {
      dispatch(fetchAgentById({ tenantId, agentId }) as any);
    },
    [dispatch, tenantId]
  );

  const create = useCallback(
    (payload: any) => dispatch(createAgent({ tenantId, payload }) as any),
    [dispatch, tenantId]
  );

  const update = useCallback(
    (agentId: string, payload: any) => dispatch(updateAgent({ tenantId, agentId, payload }) as any),
    [dispatch, tenantId]
  );

  const remove = useCallback(
    (agentId: string) => dispatch(deleteAgent({ tenantId, agentId }) as any),
    [dispatch, tenantId]
  );

  const clone = useCallback(
    (agentId: string, newName: string, newDescription?: string) =>
      dispatch(cloneAgent({ tenantId, agentId, newName, newDescription }) as any),
    [dispatch, tenantId]
  );

  const publish = useCallback(
    (agentId: string) => dispatch(publishAgent({ tenantId, agentId }) as any),
    [dispatch, tenantId]
  );

  const clearSelected = useCallback(() => {
    dispatch(clearSelectedAgent());
  }, [dispatch]);

  return {
    agents: items,
    stats,
    selectedAgent,
    loading,
    statsLoading,
    error,
    // Operations
    getAgentById,
    create,
    update,
    remove,
    clone,
    publish,
    clearSelected,
  };
}
