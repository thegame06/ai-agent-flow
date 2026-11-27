import type { NavSectionProps } from 'src/components/nav-section';

import { paths } from 'src/routes/paths';

type NavData = NavSectionProps['data'];
type NavItem = NavData[number]['items'][number];

/**
 * Menu entries that belong to a sellable app, keyed by path. An entry is shown when ANY of
 * its modules is enabled for the tenant. Entries not listed here are platform features and
 * are always shown. Module ids match AppModules in the backend (AgentFlow.Extensions).
 */
export const APP_GATES: Record<string, string[]> = {
  [paths.dashboard.threads]: ['conversations'],
  [paths.dashboard.inbox]: ['conversations'],
  [paths.dashboard.campaigns]: ['campaigns'],
  [paths.dashboard.workflows]: ['workflows'],
  [paths.dashboard.commerce]: ['communication-inbox', 'inventory', 'sales-pos', 'billing'],
};

function isAllowed(path: string, enabled: Set<string>): boolean {
  const modules = APP_GATES[path];
  return !modules || modules.some((m) => enabled.has(m));
}

function filterItem(item: NavItem, enabled: Set<string>): NavItem | null {
  if (!isAllowed(item.path, enabled)) return null;
  if (!item.children) return item;

  const children = item.children
    .map((child) => filterItem(child as NavItem, enabled))
    .filter((child): child is NavItem => child !== null);

  return { ...item, children };
}

/**
 * Removes menu entries for apps the tenant does not have, and sections left empty.
 * With `enabled = null` (still loading, or the request failed) the menu is returned as is.
 */
export function filterNavByApps(data: NavData, enabled: Set<string> | null): NavData {
  if (!enabled) return data;

  return data
    .map((section) => ({
      ...section,
      items: section.items
        .map((item) => filterItem(item, enabled))
        .filter((item): item is NavItem => item !== null),
    }))
    .filter((section) => section.items.length > 0);
}
