import type { NavSectionProps } from 'src/components/nav-section';

import { paths } from 'src/routes/paths';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

const icon = (name: string) => <Iconify icon={name} width={24} />;

const ICONS = {
  dashboard: icon('mdi:view-dashboard-outline'),
  workflow: icon('mdi:source-branch'),
  agent: icon('mdi:robot-happy-outline'),
  intent: icon('mdi:target-variant'),
  inbox: icon('mdi:inbox-outline'),
  commerce: icon('mdi:store-cog-outline'),
  executions: icon('mdi:chart-timeline-variant'),
  humanReview: icon('mdi:account-supervisor-outline'),
  kycPayments: icon('mdi:shield-account-outline'),
  channels: icon('mdi:access-point'),
  marketplace: icon('mdi:storefront-outline'),
  mcp: icon('mdi:connection'),
  settings: icon('mdi:cog-outline'),
};

// ----------------------------------------------------------------------

export const navData: NavSectionProps['data'] = [
  {
    subheader: 'Inicio',
    items: [
      {
        title: 'Inicio',
        path: paths.dashboard.overview,
        icon: ICONS.dashboard,
      },
    ],
  },
  {
    subheader: 'Construccion',
    items: [
      {
        title: 'Workflow Studio',
        path: paths.dashboard.workflows,
        icon: ICONS.workflow,
      },
      {
        title: 'Agentes',
        path: paths.dashboard.agents,
        icon: ICONS.agent,
      },
      {
        title: 'Intenciones',
        path: paths.dashboard.intentMap,
        icon: ICONS.intent,
      },
    ],
  },
  {
    subheader: 'Operacion',
    items: [
      {
        title: 'Bandeja de entrada',
        path: paths.dashboard.threads,
        icon: ICONS.inbox,
      },
      {
        title: 'Commerce',
        path: paths.dashboard.commerce,
        icon: ICONS.commerce,
      },
      {
        title: 'Ejecuciones',
        path: paths.dashboard.executions,
        icon: ICONS.executions,
      },
      {
        title: 'Revision humana',
        path: paths.dashboard.checkpoints,
        icon: ICONS.humanReview,
      },
      {
        title: 'KYC y pagos',
        path: paths.dashboard.kycPayments,
        icon: ICONS.kycPayments,
      },
    ],
  },
  {
    subheader: 'Integraciones',
    items: [
      {
        title: 'Canales',
        path: paths.dashboard.system.channels,
        icon: ICONS.channels,
      },
      {
        title: 'Marketplace',
        path: paths.dashboard.marketplace,
        icon: ICONS.marketplace,
      },
      {
        title: 'MCP',
        path: paths.dashboard.system.mcp,
        icon: ICONS.mcp,
      },
    ],
  },
  {
    subheader: 'Administracion',
    items: [
      {
        title: 'Configuracion',
        path: paths.dashboard.system.settings,
        icon: ICONS.settings,
        children: [
          { title: 'Modelos', path: paths.dashboard.system.models },
          { title: 'Auth profiles', path: paths.dashboard.system.authProfiles },
          { title: 'Segmentos', path: paths.dashboard.system.segmentRouting },
          { title: 'Feature flags', path: paths.dashboard.system.featureFlags },
          { title: 'Politicas', path: paths.dashboard.governance.policies },
          { title: 'Auditoria', path: paths.dashboard.governance.audit },
        ],
      },
    ],
  },
];
