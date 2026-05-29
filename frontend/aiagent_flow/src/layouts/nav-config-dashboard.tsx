import type { NavSectionProps } from 'src/components/nav-section';

import { paths } from 'src/routes/paths';

import { Iconify } from 'src/components/iconify';

const icon = (name: string) => <Iconify icon={name} width={24} />;

const ICONS = {
  home: icon('mdi:home-outline'),
  automation: icon('mdi:auto-fix'),
  workflow: icon('mdi:source-branch'),
  runtime: icon('mdi:layers-triple-outline'),
  agent: icon('mdi:robot-happy-outline'),
  intent: icon('mdi:target-variant'),
  inbox: icon('mdi:forum-outline'),
  unclassified: icon('mdi:inbox-outline'),
  commerce: icon('mdi:store-cog-outline'),
  activity: icon('mdi:chart-timeline-variant'),
  humanReview: icon('mdi:account-supervisor-outline'),
  kycPayments: icon('mdi:shield-account-outline'),
  channels: icon('mdi:access-point'),
  integrations: icon('mdi:storefront-outline'),
  tools: icon('mdi:tools'),
  advanced: icon('mdi:connection'),
  settings: icon('mdi:cog-outline'),
};

export const navData: NavSectionProps['data'] = [
  {
    subheader: 'Inicio',
    items: [
      {
        title: 'Inicio',
        path: paths.dashboard.overview,
        icon: ICONS.home,
      },
    ],
  },
  {
    subheader: 'Operacion',
    items: [
      {
        title: 'Bandeja',
        path: paths.dashboard.threads,
        icon: ICONS.inbox,
        children: [
          { title: 'Conversaciones', path: paths.dashboard.threads },
          { title: 'Casos sin clasificar', path: paths.dashboard.inbox },
        ],
      },
      {
        title: 'Casos por revisar',
        path: paths.dashboard.checkpoints,
        icon: ICONS.humanReview,
      },
      {
        title: 'Ventas y cobros',
        path: paths.dashboard.commerce,
        icon: ICONS.commerce,
      },
      {
        title: 'Actividad',
        path: paths.dashboard.executions,
        icon: ICONS.activity,
      },
      {
        title: 'KYC y pagos',
        path: paths.dashboard.kycPayments,
        icon: ICONS.kycPayments,
      },
    ],
  },
  {
    subheader: 'Construccion',
    items: [
      {
        title: 'Crear automatizacion',
        path: paths.dashboard.annonai,
        icon: ICONS.automation,
      },
      {
        title: 'Automatizaciones',
        path: paths.dashboard.workflows,
        icon: ICONS.workflow,
        children: [
          { title: 'Ver automatizaciones', path: paths.dashboard.workflows },
          { title: 'Runtime avanzado', path: paths.dashboard.runtimeStudio('text') },
        ],
      },
      {
        title: 'Asistentes',
        path: paths.dashboard.agents,
        icon: ICONS.agent,
      },
      {
        title: 'Intenciones',
        path: paths.dashboard.intents,
        icon: ICONS.intent,
      },
    ],
  },
  {
    subheader: 'Conexiones',
    items: [
      {
        title: 'Canales',
        path: paths.dashboard.system.channels,
        icon: ICONS.channels,
      },
      {
        title: 'Integraciones',
        path: paths.dashboard.marketplace,
        icon: ICONS.integrations,
      },
      {
        title: 'Herramientas',
        path: paths.dashboard.tools,
        icon: ICONS.tools,
        children: [
          { title: 'Herramientas', path: paths.dashboard.tools },
          { title: 'Conectores avanzados', path: paths.dashboard.system.mcp },
        ],
      },
    ],
  },
  {
    subheader: 'Administracion',
    items: [
      {
        title: 'Configuracion general',
        path: paths.dashboard.system.settings,
        icon: ICONS.settings,
        children: [
          { title: 'Configuracion general', path: paths.dashboard.system.settings },
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
