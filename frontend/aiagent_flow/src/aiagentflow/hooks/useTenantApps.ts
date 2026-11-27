import { useState, useEffect } from 'react';

import axios from 'src/lib/axios';

import { useTenantId } from './useTenantId';

export type TenantAppModule = {
  moduleId: string;
  name: string;
  app: string;
  enabled: boolean;
  source: 'explicit' | 'default' | 'quarantined';
};

/**
 * Effective app modules for the current tenant (GET /api/v1/extensions/tenants/{tenantId}/apps).
 * Returns null while loading or if the request fails: callers then show everything and let the
 * API, which enforces the same permissions, answer 403 for disabled apps.
 */
export function useTenantApps(): Set<string> | null {
  const tenantId = useTenantId();
  const [enabled, setEnabled] = useState<Set<string> | null>(null);

  useEffect(() => {
    let active = true;

    axios
      .get<TenantAppModule[]>(`/api/v1/extensions/tenants/${tenantId}/apps`)
      .then((res) => {
        if (!active) return;
        const modules = Array.isArray(res.data) ? res.data : [];
        setEnabled(new Set(modules.filter((m) => m.enabled).map((m) => m.moduleId)));
      })
      .catch(() => {
        if (active) setEnabled(null);
      });

    return () => {
      active = false;
    };
  }, [tenantId]);

  return enabled;
}
