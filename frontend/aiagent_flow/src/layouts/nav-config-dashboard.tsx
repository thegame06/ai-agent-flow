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
  /**
   * Command Center
   */
  {
    subheader: 'Command Center',
    items: [
      {
        title: 'Overview',
        path: paths.dashboard.overview,
        icon: ICONS.dashboard,
      },
      {
        title: 'Agents',
        path: paths.dashboard.agents,
        icon: ICONS.user,
        info: <Label color="info">v{CONFIG.appVersion}</Label>,
      },
      {
        title: 'Executions',
        path: paths.dashboard.executions,
        icon: ICONS.analytics,
      },
      {
        title: 'Review Queue',
        path: paths.dashboard.checkpoints,
        icon: ICONS.order,
        info: <Label color="warning">HITL</Label>,
      },
      {
        title: 'Extensions / Tools',
        path: paths.dashboard.tools,
        icon: ICONS.parameter,
      },
    ],
  },
  /**
   * Governance
   */
  {
    subheader: 'Governance',
    items: [
      {
        title: 'Policy Engine',
        path: paths.dashboard.governance.root,
        icon: ICONS.lock,
        children: [
          { title: 'Policies', path: paths.dashboard.governance.policies },
          { title: 'Audit Trail', path: paths.dashboard.governance.audit },
        ],
      },
    ],
  },
  /**
   * Infrastructure
   */
  {
    subheader: 'Infrastructure',
    items: [
      {
        title: 'Model Routing',
        path: paths.dashboard.system.models,
        icon: ICONS.product,
      },
      {
        title: 'Auth Profiles',
        path: paths.dashboard.system.authProfiles,
        icon: ICONS.lock,
      },
      {
        title: 'MCP Console',
        path: paths.dashboard.system.mcp,
        icon: ICONS.parameter,
      },
      {
        title: 'Channels',
        path: paths.dashboard.system.channels,
        icon: ICONS.channel,
      },
      {
        title: 'Settings',
        path: paths.dashboard.system.settings,
        icon: ICONS.parameter,
      },
    ],
  },
];
