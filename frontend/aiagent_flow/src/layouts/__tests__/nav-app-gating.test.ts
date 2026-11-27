import { it, expect, describe } from 'vitest';

import { paths } from 'src/routes/paths';

import { filterNavByApps } from '../nav-app-gating';

const nav = [
  {
    subheader: 'Operacion',
    items: [
      {
        title: 'Bandeja',
        path: paths.dashboard.threads,
        children: [
          { title: 'Conversaciones', path: paths.dashboard.threads },
          { title: 'Casos sin clasificar', path: paths.dashboard.inbox },
        ],
      },
      { title: 'Casos por revisar', path: paths.dashboard.checkpoints },
      { title: 'Ventas y cobros', path: paths.dashboard.commerce },
      { title: 'Campanas', path: paths.dashboard.campaigns },
    ],
  },
  {
    subheader: 'Construccion',
    items: [{ title: 'Automatizaciones', path: paths.dashboard.workflows }],
  },
];

const titles = (data: ReturnType<typeof filterNavByApps>) =>
  data.flatMap((section) => section.items.map((item) => item.title));

describe('filterNavByApps', () => {
  it('returns the menu unchanged while entitlements are unknown', () => {
    expect(filterNavByApps(nav, null)).toBe(nav);
  });

  it('hides disabled apps and keeps platform entries', () => {
    const result = filterNavByApps(nav, new Set(['conversations', 'workflows']));

    expect(titles(result)).toEqual(['Bandeja', 'Casos por revisar', 'Automatizaciones']);
  });

  it('shows commerce when any commerce module is enabled', () => {
    const result = filterNavByApps(nav, new Set(['inventory']));

    expect(titles(result)).toContain('Ventas y cobros');
    expect(titles(result)).not.toContain('Campanas');
  });

  it('drops sections left without items', () => {
    const result = filterNavByApps(nav, new Set());

    expect(result.map((section) => section.subheader)).toEqual(['Operacion']);
    expect(titles(result)).toEqual(['Casos por revisar']);
  });
});
