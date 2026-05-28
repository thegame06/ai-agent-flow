import { useParams } from 'react-router';
import { useMemo, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { RuntimeEntityCards } from './components/RuntimeEntityCards';
import { RuntimeModelProfilesPanel } from './components/RuntimeModelProfilesPanel';
import { RuntimeWorkspaceShell } from './components/RuntimeWorkspaceShell';

const runtimeMap: Record<string, { label: 'Text' | 'Voice' | 'MultimodalRealtime'; slug: string }> = {
  text: { label: 'Text', slug: 'text' },
  voice: { label: 'Voice', slug: 'voice' },
  multimodal: { label: 'MultimodalRealtime', slug: 'multimodal' },
};

export default function RuntimeStudioPage() {
  const tenantId = useTenantId();
  const runtimeStorageKey = `af:runtimeStudio:runtimeKind:${tenantId}`;
  const { runtimeKind = 'text' } = useParams();
  const storedRuntime =
    typeof window !== 'undefined' ? localStorage.getItem(runtimeStorageKey)?.toLowerCase() : null;
  const runtime = runtimeMap[(runtimeKind || storedRuntime || 'text').toLowerCase()] ?? runtimeMap.text;
  useEffect(() => {
    if (typeof window === 'undefined') return;
    localStorage.setItem(runtimeStorageKey, runtime.label);
  }, [runtime.label, runtimeStorageKey]);
  const runtimeQuery = `runtimeKind=${encodeURIComponent(runtime.label)}`;

  const items = useMemo(
    () => [
      {
        title: 'Asistentes',
        description: 'Gestiona asistentes del runtime seleccionado.',
        href: `${paths.dashboard.agents}?${runtimeQuery}`,
        icon: 'mdi:robot-outline',
      },
      {
        title: 'Workflows de negocio',
        description: 'Diseña flujos principales para este runtime.',
        href: `${paths.dashboard.workflows}?${runtimeQuery}`,
        icon: 'mdi:source-branch',
      },
      {
        title: 'Subflujos',
        description: 'Crea subflujos reutilizables y especializados por modalidad.',
        href: `${paths.dashboard.automationNew}?wizard=agentSubflow&${runtimeQuery}`,
        icon: 'mdi:vector-polyline',
      },
    ],
    [runtimeQuery]
  );

  return (
    <>
      <Helmet>
        <title>Runtime Studio | {CONFIG.appName}</title>
      </Helmet>
      <RuntimeWorkspaceShell
        title={`Runtime Studio · ${runtime.label}`}
        description="Espacio por modalidad para asistentes, workflows de negocio y subflujos reutilizables."
        runtimeKind={runtime.label}
        actions={[
          { label: 'Text', href: paths.dashboard.runtimeStudio('text'), icon: 'mdi:form-textbox', variant: runtime.slug === 'text' ? 'contained' : 'outlined' },
          { label: 'Voice', href: paths.dashboard.runtimeStudio('voice'), icon: 'mdi:phone-in-talk-outline', variant: runtime.slug === 'voice' ? 'contained' : 'outlined' },
          { label: 'Multimodal', href: paths.dashboard.runtimeStudio('multimodal'), icon: 'mdi:video-wireless-outline', variant: runtime.slug === 'multimodal' ? 'contained' : 'outlined' },
        ]}
      >
        <RuntimeEntityCards items={items} />
        <RuntimeModelProfilesPanel tenantId={tenantId} runtimeKind={runtime.label} />
      </RuntimeWorkspaceShell>
    </>
  );
}
