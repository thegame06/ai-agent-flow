import { useMemo } from 'react';

import { useAuthContext } from 'src/auth/hooks';

const FALLBACK_TENANT = 'tenant-1';

export function useTenantId() {
  const { user } = useAuthContext();

  return useMemo(() => {
    const fromUser = (user as any)?.tenantId || (user as any)?.tenant?.id;
    const fromStorage = typeof window !== 'undefined' ? localStorage.getItem('af:tenantId') : null;
    const fromEnv = import.meta.env.VITE_DEFAULT_TENANT_ID as string | undefined;

    const tenantId = fromUser || fromStorage || fromEnv || FALLBACK_TENANT;

    if (typeof window !== 'undefined' && tenantId) {
      localStorage.setItem('af:tenantId', tenantId);
    }

    return tenantId;
  }, [user]);
}
