import { CONFIG } from 'src/global-config';

import type { WorkspacesPopoverProps } from './components/workspaces-popover';

// ----------------------------------------------------------------------

export const _workspaces: WorkspacesPopoverProps['data'] = [
  {
    id: 'team-1',
    name: 'Annonai',
    logo: `${CONFIG.assetsDir}/assets/icons/workspaces/logo-1.webp`,
    plan: 'Workspace',
  },
];
