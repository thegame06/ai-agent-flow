import type { AiAgentNodeConfig } from './types';

export const ALLOWED_ACTIVITY_TYPES = [
  'ai.agent',
  'connect.send_whatsapp_template',
  'connect.update_inbox_status',
  'connect.enqueue_campaign_message',
] as const;

export const ACTIVITY_TYPE_PRESETS: Record<string, Record<string, string>> = {
  'connect.send_whatsapp_template': {
    recipient: '{{payload.recipient}}',
    content: 'Hola {{payload.customerName}}',
    channel: '{{payload.channel}}',
  },
  'connect.update_inbox_status': {
    messageId: '{{steps.send-wa.inboxMessageId}}',
    status: 'Sent',
  },
  'connect.enqueue_campaign_message': {
    recipient: '{{payload.recipient}}',
    content: 'Campaign workflow message',
    channel: 'whatsapp',
  },
  'ai.agent': {},
};

export const DEFAULT_AI_AGENT_CONFIG: AiAgentNodeConfig = {
  model: 'gpt-4o',
  instructions: '',
  tools: [],
  context: '',
  knowledge: [],
  fallbackModel: 'gpt-4o-mini',
  maxLatencyMs: 3000,
  maxCostUsd: 0.05,
  dlpEnabled: true,
  temperature: 0.2,
  maxTokens: 800,
};

export const DEFAULT_DEFINITION = JSON.stringify(
  {
    activities: [
      {
        id: 'send-wa',
        type: 'connect.send_whatsapp_template',
        timeoutMs: 10000,
        retryCount: 1,
        retryDelayMs: 1000,
        when: { key: 'channel', equals: 'whatsapp' },
        config: {
          recipient: '{{payload.recipient}}',
          content: 'Hola {{payload.customerName}}',
          channel: '{{payload.channel}}',
        },
        onSuccess: 'mark-sent',
      },
      {
        id: 'mark-sent',
        type: 'connect.update_inbox_status',
        config: {
          messageId: '{{steps.send-wa.inboxMessageId}}',
          status: 'Sent',
        },
      },
    ],
  },
  null,
  2
);
