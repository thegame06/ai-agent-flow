// Inbox by customer: one row per person, whatever channels they used, and a merged timeline.

export type InboxChannelRef = {
  channelId: string;
  channelName: string;
  channelType: string;
  provider: string;
  businessAddress?: string | null;
  sessionId: string;
  lastActivityAt: string;
  channelDeleted: boolean;
};

export type InboxCustomer = {
  key: string;
  partyId?: string | null;
  name: string;
  registered: boolean;
  address: string;
  channels: InboxChannelRef[];
  lastMessage?: string | null;
  lastDirection?: string | null;
  lastChannel?: InboxChannelRef | null;
  lastActivityAt: string;
  unread: number;
  replyPending: boolean;
  handoff?: 'pending' | 'human' | null;
  onlyDeletedChannels: boolean;
};

export type InboxTimelineItem = {
  id: string;
  direction: 'in' | 'out';
  actor: string;
  content: string;
  createdAt: string;
  status: string;
  delivery?: string | null;
  error?: string | null;
  channelId: string;
  channelName: string;
  provider: string;
  sessionId: string;
  attachments?: string | null;
};

/** How each provider is shown: WhatsApp through QR, Meta or Twilio; the API; web chat. */
export const PROVIDERS: Record<string, { label: string; icon: string; color: 'success' | 'info' | 'secondary' | 'warning' | 'default' }> = {
  qr: { label: 'WhatsApp · QR', icon: 'mdi:qrcode', color: 'success' },
  meta: { label: 'WhatsApp · Meta', icon: 'mdi:whatsapp', color: 'success' },
  twilio: { label: 'WhatsApp · Twilio', icon: 'mdi:whatsapp', color: 'info' },
  api: { label: 'API', icon: 'mdi:api', color: 'secondary' },
  webchat: { label: 'Web chat', icon: 'mdi:chat-processing-outline', color: 'warning' },
  voice: { label: 'Voz', icon: 'mdi:phone-in-talk-outline', color: 'default' },
  callcenter: { label: 'Call center', icon: 'mdi:headset', color: 'default' },
};

export const providerOf = (provider?: string | null) =>
  PROVIDERS[(provider ?? '').toLowerCase()] ?? { label: provider || 'Canal', icon: 'mdi:message-outline', color: 'default' as const };

export const ACTOR_LABEL: Record<string, string> = {
  customer: 'Cliente',
  assistant: 'Asistente',
  bot: 'Asistente',
  agent: 'Asistente',
  human: 'Asesor',
  campaign: 'Campaña',
  workflow: 'Automatización',
  system: 'Sistema',
};

export const initials = (name: string) =>
  name
    .replace(/[^\p{L}\s]/gu, ' ')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]!.toUpperCase())
    .join('') || '#';

export const shortTime = (iso: string) => {
  const d = new Date(iso);
  const now = new Date();
  const sameDay = d.toDateString() === now.toDateString();
  return sameDay
    ? d.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('es-MX', { day: '2-digit', month: 'short' });
};

export const fullTime = (iso: string) =>
  new Date(iso).toLocaleString('es-MX', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });

export const attachmentsOf = (
  raw?: string | null
): { kind: string; fileName?: string | null; url?: string | null; mediaId?: string | null }[] => {
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    // Stored by the API as PascalCase (Kind, FileName, Url).
    return parsed.map((a: any) => ({
      kind: a.kind ?? a.Kind ?? 'file',
      fileName: a.fileName ?? a.FileName,
      url: a.url ?? a.Url,
      mediaId: a.mediaId ?? a.MediaId,
    }));
  } catch {
    return [];
  }
};
