// WhatsApp (Meta) message templates: types and client-side rendering shared by the
// templates page and the campaign wizard.

export type TemplateButton = { type: string; text: string; url?: string | null; phoneNumber?: string | null };

export type TemplateComponent = {
  type: 'HEADER' | 'BODY' | 'FOOTER' | 'BUTTONS' | string;
  format?: string | null;
  text?: string | null;
  buttons?: TemplateButton[] | null;
};

export type TemplateParameter = {
  key: string;
  component: string;
  source: 'field' | 'static' | string;
  field?: string | null;
  staticValue?: string | null;
  example?: string | null;
};

export type StatusChange = { status: string; reason?: string | null; source: string; at: string };

export type WaTemplate = {
  id: string;
  connectionId: string;
  businessAccountId?: string | null;
  metaTemplateId?: string | null;
  name: string;
  language: string;
  category: string;
  status: string;
  rejectedReason?: string | null;
  qualityScore?: string | null;
  parameterFormat: string;
  components: TemplateComponent[];
  parameters: TemplateParameter[];
  version: number;
  origin: string;
  statusHistory: StatusChange[];
  lastSyncedAt?: string | null;
  updatedAt: string;
};

export type TemplateField = { key: string; label: string; example: string; group: string };

export type { ChannelMeta } from '../channels/WhatsAppMetaDialog';

export const TEMPLATE_STATUS: Record<string, { label: string; color: 'success' | 'warning' | 'error' | 'default' | 'info' }> = {
  APPROVED: { label: 'Aprobada', color: 'success' },
  PENDING: { label: 'En revisión', color: 'warning' },
  IN_APPEAL: { label: 'En apelación', color: 'warning' },
  REJECTED: { label: 'Rechazada', color: 'error' },
  PAUSED: { label: 'Pausada', color: 'default' },
  DISABLED: { label: 'Deshabilitada', color: 'default' },
  DELETED: { label: 'Eliminada', color: 'default' },
};

export const CATEGORY_LABEL: Record<string, string> = {
  UTILITY: 'Utilidad',
  MARKETING: 'Marketing',
  AUTHENTICATION: 'Autenticación',
};

export const QUALITY: Record<string, { label: string; color: 'success' | 'warning' | 'error' | 'default' }> = {
  GREEN: { label: 'Alta', color: 'success' },
  YELLOW: { label: 'Media', color: 'warning' },
  RED: { label: 'Baja', color: 'error' },
  UNKNOWN: { label: 'Sin datos', color: 'default' },
};

export const statusOf = (s: string) => TEMPLATE_STATUS[s] ?? { label: s, color: 'default' as const };

const PLACEHOLDER = /\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g;

export const placeholders = (text?: string | null) =>
  Array.from(new Set(Array.from((text ?? '').matchAll(PLACEHOLDER)).map((m) => m[1])));

/** Every slot a value goes into: header/body placeholders, media header, URL buttons. */
export function slots(components: TemplateComponent[]): { component: string; key: string }[] {
  const result: { component: string; key: string }[] = [];
  components.forEach((c) => {
    const type = c.type.toUpperCase();
    if (type === 'HEADER' && (!c.format || c.format === 'TEXT')) placeholders(c.text).forEach((key) => result.push({ component: 'header', key }));
    else if (type === 'HEADER') result.push({ component: 'header_media', key: 'media' });
    else if (type === 'BODY') placeholders(c.text).forEach((key) => result.push({ component: 'body', key }));
    else if (type === 'BUTTONS')
      (c.buttons ?? []).forEach((b, i) => {
        if (b.type.toUpperCase() === 'URL') placeholders(b.url).forEach((key) => result.push({ component: `button:${i}`, key }));
      });
  });
  return result;
}

/** The template as the customer reads it, filled with the given variables (or the examples). */
export function renderTemplate(template: Pick<WaTemplate, 'components' | 'parameters'>, variables?: Record<string, string> | null) {
  const value = (component: string, key: string) => {
    const p = template.parameters.find((x) => x.component === component && x.key === key);
    if (!p) return `{{${key}}}`;
    if (p.source === 'static') return p.staticValue || p.example || `{{${key}}}`;
    const v = p.field ? variables?.[p.field] : undefined;
    return v || (variables ? `{{${p.field ?? key}}}` : p.example || `{{${p.field ?? key}}}`);
  };
  const fill = (text: string | null | undefined, component: string) =>
    (text ?? '').replace(PLACEHOLDER, (_m, key: string) => value(component, key));

  const header = template.components.find((c) => c.type.toUpperCase() === 'HEADER');
  const body = template.components.find((c) => c.type.toUpperCase() === 'BODY');
  const footer = template.components.find((c) => c.type.toUpperCase() === 'FOOTER');
  const buttons = template.components.find((c) => c.type.toUpperCase() === 'BUTTONS')?.buttons ?? [];
  return {
    header: header ? (header.format && header.format !== 'TEXT' ? `[${header.format.toLowerCase()}]` : fill(header.text, 'header')) : '',
    body: fill(body?.text, 'body'),
    footer: footer?.text ?? '',
    buttons: buttons.map((b) => b.text),
  };
}

/** Placeholders a contact has no value for (the template would not be sent to them). */
export function missingFor(template: Pick<WaTemplate, 'components' | 'parameters'>, variables?: Record<string, string> | null) {
  return slots(template.components).flatMap(({ component, key }) => {
    const p = template.parameters.find((x) => x.component === component && x.key === key);
    if (!p) return [`${component} {{${key}}} sin mapear`];
    if (p.source === 'static') return p.staticValue ? [] : [`valor fijo de ${component} {{${key}}}`];
    return p.field && variables?.[p.field] ? [] : [p.field ?? `${component} {{${key}}}`];
  });
}
