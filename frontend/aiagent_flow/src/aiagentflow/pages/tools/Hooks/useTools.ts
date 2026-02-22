import { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import { fetchTools } from '../Redux/Slice';

export function useTools() {
  const dispatch = useDispatch();
  const { items, loading, error } = useSelector((state: any) => state.tools);

  useEffect(() => {
    dispatch(fetchTools() as any);
  }, [dispatch]);

  return {
    tools: items,
    loading,
    error,
  };
}
