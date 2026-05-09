import type { NavSectionProps } from 'src/components/nav-section';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';

import { Label } from 'src/components/label';
import { SvgColor } from 'src/components/svg-color';

// ----------------------------------------------------------------------

const icon = (name: string) => (
  <SvgColor src={`${CONFIG.assetsDir}/assets/icons/navbar/${name}.svg`} />
);

const ICONS = {
  job: icon('ic-job'),
  blog: icon('ic-blog'),
  chat: icon('ic-chat'),
  mail: icon('ic-mail'),
  user: icon('ic-user'),
  file: icon('ic-file'),
  lock: icon('ic-lock'),
  tour: icon('ic-tour'),
  order: icon('ic-order'),
  label: icon('ic-label'),
  blank: icon('ic-blank'),
  kanban: icon('ic-kanban'),
  folder: icon('ic-folder'),
  course: icon('ic-course'),
  banking: icon('ic-banking'),
  booking: icon('ic-booking'),
  invoice: icon('ic-invoice'),
  product: icon('ic-product'),
  calendar: icon('ic-calendar'),
  disabled: icon('ic-disabled'),
  external: icon('ic-external'),
  menuItem: icon('ic-menu-item'),
  ecommerce: icon('ic-ecommerce'),
  analytics: icon('ic-analytics'),
  channel: icon('ic-chat'),
  dashboard: icon('ic-dashboard'),
  parameter: icon('ic-parameter'),
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
        title: 'Brain Studio',
        path: paths.dashboard.workflows,
        icon: ICONS.kanban,
        info: <Label color="info">Studio</Label>,
      },
      {
        title: 'Agentes',
        path: paths.dashboard.agents,
        icon: ICONS.user,
      },
      {
        title: 'Ejecuciones',
        path: paths.dashboard.executions,
        icon: ICONS.analytics,
      },
      {
        title: 'Revision humana',
        path: paths.dashboard.checkpoints,
        icon: ICONS.order,
        info: <Label color="warning">HITL</Label>,
      },
    ],
  },
  {
    subheader: 'Gestion',
    items: [
      {
        title: 'Bandeja de entrada',
        path: paths.dashboard.threads,
        icon: ICONS.chat,
      },
      {
        title: 'KYC y pagos',
        path: paths.dashboard.kycPayments,
        icon: ICONS.lock,
      },
    ],
  },
  {
    subheader: 'Integraciones',
    items: [
      {
        title: 'Marketplace',
        path: paths.dashboard.marketplace,
        icon: ICONS.product,
      },
      {
        title: 'Conectores',
        path: paths.dashboard.tools,
        icon: ICONS.parameter,
        children: [
          { title: 'Tools', path: paths.dashboard.tools },
          { title: 'Canales', path: paths.dashboard.system.channels },
          { title: 'MCP', path: paths.dashboard.system.mcp },
        ],
      },
    ],
  },
  {
    subheader: 'Administracion',
    items: [
      {
        title: 'Configuracion',
        path: paths.dashboard.system.settings,
        icon: ICONS.parameter,
        children: [
          { title: 'Modelos', path: paths.dashboard.system.models },
          { title: 'Auth profiles', path: paths.dashboard.system.authProfiles },
          { title: 'Segmentos', path: paths.dashboard.system.segmentRouting },
          { title: 'Feature flags', path: paths.dashboard.system.featureFlags },
          { title: 'Politicas', path: paths.dashboard.governance.policies },
          { title: 'Auditoria', path: paths.dashboard.governance.audit },
          { title: 'Evaluaciones', path: paths.dashboard.evaluations },
          { title: 'Orquestacion', path: paths.dashboard.orchestration },
        ],
      },
    ],
  },
];
