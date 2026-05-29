import { useParams } from 'react-router';
import { useMemo, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { RuntimeEntityCards } from './components/RuntimeEntityCards';
import { RuntimeWorkspaceShell } from './components/RuntimeWorkspaceShell';
import { RuntimeModelProfilesPanel } from './components/RuntimeModelProfilesPanel';

const runtimeMap: Record<
  string,
  { label: 'Text' | 'Voice' | 'MultimodalRealtime'; slug: string; uiLabel: string }
> = {
  text: { label: 'Text', slug: 'text', uiLabel: 'Texto' },
  voice: { label: 'Voice', slug: 'voice', uiLabel: 'Voz' },
  multimodal: {
    label: 'MultimodalRealtime',
    slug: 'multimodal',
    uiLabel: 'Multimodal',
  },
};

export default function RuntimeStudioPage() {
  const tenantId = useTenantId();
  const runtimeStorageKey = `af:runtimeStudio:runtimeKind:${tenantId}`;
  const { runtimeKind = 'text' } = useParams();
  const storedRuntime =
    typeof window !== 'undefined' ? localStorage.getItem(runtimeStorageKey)?.toLowerCase() : null;
  const runtime =
    runtimeMap[(runtimeKind || storedRuntime || 'text').toLowerCase()] ?? runtimeMap.text;

  useEffect(() => {
    if (typeof window === 'undefined') return;
    localStorage.setItem(runtimeStorageKey, runtime.label);
  }, [runtime.label, runtimeStorageKey]);

  const runtimeQuery = `runtimeKind=${encodeURIComponent(runtime.label)}`;

  const items = useMemo(
    () => [
      {
        title: 'Asistentes',
        description: 'Gestiona asistentes para esta modalidad.',
        href: `${paths.dashboard.agents}?${runtimeQuery}`,
        icon: 'mdi:robot-outline',
      },
      {
        title: 'Automatizaciones principales',
        description: 'Disena los flujos principales para esta modalidad.',
        href: `${paths.dashboard.workflows}?${runtimeQuery}`,
        icon: 'mdi:source-branch',
      },
      {
        title: 'Subflujos reutilizables',
        description: 'Crea piezas especializadas para reutilizar en otras automatizaciones.',
        href: `${paths.dashboard.automationNew}?wizard=agentSubflow&${runtimeQuery}`,
        icon: 'mdi:vector-polyline',
      },
      {
        title: 'Centro de pruebas',
        description: 'Valida sesiones, mensajes y eventos antes de salir a produccion.',
        href: paths.dashboard.runtimeTestStudio(runtime.slug as 'text' | 'voice' | 'multimodal'),
        icon: 'mdi:test-tube',
      },
    ],
    [runtime.slug, runtimeQuery]
  );

  return (
    <>
      <Helmet>
        <title>Runtime avanzado | {CONFIG.appName}</title>
      </Helmet>
      <RuntimeWorkspaceShell
        title={`Runtime avanzado · ${runtime.uiLabel}`}
        description="Espacio por modalidad para asistentes, automatizaciones reutilizables y pruebas operativas."
        runtimeKind={runtime.label}
        actions={[
          {
            label: 'Texto',
            href: paths.dashboard.runtimeStudio('text'),
            icon: 'mdi:form-textbox',
            variant: runtime.slug === 'text' ? 'contained' : 'outlined',
          },
          {
            label: 'Voz',
            href: paths.dashboard.runtimeStudio('voice'),
            icon: 'mdi:phone-in-talk-outline',
            variant: runtime.slug === 'voice' ? 'contained' : 'outlined',
          },
          {
            label: 'Multimodal',
            href: paths.dashboard.runtimeStudio('multimodal'),
            icon: 'mdi:video-wireless-outline',
            variant: runtime.slug === 'multimodal' ? 'contained' : 'outlined',
          },
        ]}
      >
        <RuntimeEntityCards items={items} />
        <RuntimeModelProfilesPanel tenantId={tenantId} runtimeKind={runtime.label} />
      </RuntimeWorkspaceShell>
    </>
  );
}
