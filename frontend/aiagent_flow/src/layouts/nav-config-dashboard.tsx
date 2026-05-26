import type { NavSectionProps } from 'src/components/nav-section';

import { paths } from 'src/routes/paths';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

const icon = (name: string) => <Iconify icon={name} width={24} />;

const ICONS = {
  dashboard: icon('mdi:view-dashboard-outline'),
  workflow: icon('mdi:source-branch'),
  runtime: icon('mdi:layers-triple-outline'),
  automation: icon('mdi:auto-fix'),
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
        title: 'Flujos automatizados',
        path: paths.dashboard.workflows,
        icon: ICONS.workflow,
      },
      {
        title: 'Runtime Studio',
        path: paths.dashboard.runtimeStudio('text'),
        icon: ICONS.runtime,
      },
      {
        title: 'Asistentes IA',
        path: paths.dashboard.agents,
        icon: ICONS.agent,
      },
      {
        title: 'Reglas de intención',
        path: paths.dashboard.intents,
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
        icon: icon('mdi:forum-outline'),
      },
      {
        title: 'Casos sin clasificar',
        path: paths.dashboard.inbox,
        icon: ICONS.inbox,
      },
      {
        title: 'Ventas y cobros',
        path: paths.dashboard.commerce,
        icon: ICONS.commerce,
      },
      {
        title: 'Actividad del sistema',
        path: paths.dashboard.executions,
        icon: ICONS.executions,
      },
      {
        title: 'Casos para revisar',
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
        title: 'Canales de atencion',
        path: paths.dashboard.system.channels,
        icon: ICONS.channels,
      },
      {
        title: 'Integraciones',
        path: paths.dashboard.marketplace,
        icon: ICONS.marketplace,
      },
      {
        title: 'Herramientas externas',
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
          { title: 'Modelos IA', path: paths.dashboard.system.models },
          { title: 'Credenciales', path: paths.dashboard.system.authProfiles },
          { title: 'Funciones beta', path: paths.dashboard.system.featureFlags },
          { title: 'Equipos y atencion', path: paths.dashboard.system.workforce },
          { title: 'Politicas', path: paths.dashboard.governance.policies },
          { title: 'Auditoria', path: paths.dashboard.governance.audit },
          { title: 'Operaciones IA', path: paths.dashboard.governance.operations },
        ],
      },
    ],
  },
];
