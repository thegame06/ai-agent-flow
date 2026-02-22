import { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import { fetchExecutions } from '../Redux/Slice';

export function useExecutions(tenantId: string) {
  const dispatch = useDispatch();
  const { items, loading, error } = useSelector((state: any) => state.executions);

  useEffect(() => {
    if (tenantId) {
      dispatch(fetchExecutions(tenantId) as any);
    }
  }, [dispatch, tenantId]);

  return {
    executions: items,
    loading,
    error,
  };
}
