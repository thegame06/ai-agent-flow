import { useMemo, useState, useEffect } from 'react';

type Persisted<T> = {
  updatedAt: string;
  state: T;
};

export function useWizardProgress<T>(
  tenantId: string,
  wizardId: string,
  initialState: T
) {
  const storageKey = useMemo(
    () => `af:wizard:${tenantId}:${wizardId}`,
    [tenantId, wizardId]
  );

  const [state, setState] = useState<T>(() => {
    try {
      const raw = localStorage.getItem(storageKey);
      if (!raw) return initialState;
      const parsed = JSON.parse(raw) as Persisted<T>;
      return parsed?.state ?? initialState;
    } catch {
      return initialState;
    }
  });

  useEffect(() => {
    try {
      const payload: Persisted<T> = {
        updatedAt: new Date().toISOString(),
        state,
      };
      localStorage.setItem(storageKey, JSON.stringify(payload));
    } catch {
      // no-op
    }
  }, [state, storageKey]);

  const reset = () => {
    setState(initialState);
    try {
      localStorage.removeItem(storageKey);
    } catch {
      // no-op
    }
  };

  return { state, setState, reset };
}

