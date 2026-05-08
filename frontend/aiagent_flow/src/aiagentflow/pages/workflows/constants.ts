import type { AiAgentNodeConfig } from './types';

export const ALLOWED_ACTIVITY_TYPES = [
  'ai.agent',
  'connect.send_whatsapp_template',
  'connect.update_inbox_status',
  'connect.enqueue_campaign_message',
  'kyc.document_check',
  'kyc.review_case',
  'payments.create_intent',
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
  'kyc.document_check': {
    customerId: '{{payload.customerId}}',
    fullName: '{{payload.fullName}}',
    documentType: '{{payload.documentType}}',
    documentNumber: '{{payload.documentNumber}}',
  },
  'kyc.review_case': {
    caseId: '{{steps.doc-check.caseId}}',
    approved: 'true',
    notes: 'Auto-review fallback',
  },
  'payments.create_intent': {
    customerId: '{{payload.customerId}}',
    amount: '{{payload.amount}}',
    currency: '{{payload.currency}}',
    reference: '{{payload.reference}}',
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

export const WORKFLOW_QUICKSTARTS: Array<{ id: string; name: string; triggerEventName: string; definitionJson: string }> = [
  {
    id: 'wf-inbox-auto-route',
    name: 'Inbox Auto Route + Acknowledge',
    triggerEventName: 'connect.message.received',
    definitionJson: JSON.stringify(
      {
        activities: [
          {
            id: 'ai-route',
            type: 'ai.agent',
            config: {
              input: '{{payload.content}}',
              context: '{{payload.channel}}'
            },
            onSuccess: 'mark-queued'
          },
          {
            id: 'mark-queued',
            type: 'connect.update_inbox_status',
            config: {
              messageId: '{{payload.inboxMessageId}}',
              status: 'Queued'
            }
          }
        ]
      },
      null,
      2
    ),
  },
  {
    id: 'wf-kyc-document-check',
    name: 'KYC Document Check Flow',
    triggerEventName: 'kyc.document.submitted',
    definitionJson: JSON.stringify(
      {
        activities: [
          {
            id: 'doc-check',
            type: 'kyc.document_check',
            config: {
              customerId: '{{payload.customerId}}',
              fullName: '{{payload.fullName}}',
              documentType: '{{payload.documentType}}',
              documentNumber: '{{payload.documentNumber}}'
            },
            onSuccess: 'review-gate'
          },
          {
            id: 'review-gate',
            type: 'ai.agent',
            config: {
              input: 'Assess if manual review is required for KYC case {{steps.doc-check.caseId}}'
            }
          }
        ]
      },
      null,
      2
    ),
  },
  {
    id: 'wf-payment-followup',
    name: 'Payment Intent + Follow-up',
    triggerEventName: 'payments.intent.created',
    definitionJson: JSON.stringify(
      {
        activities: [
          {
            id: 'create-payment',
            type: 'payments.create_intent',
            config: {
              customerId: '{{payload.customerId}}',
              amount: '{{payload.amount}}',
              currency: '{{payload.currency}}',
              reference: '{{payload.reference}}'
            },
            onSuccess: 'notify-customer'
          },
          {
            id: 'notify-customer',
            type: 'connect.enqueue_campaign_message',
            config: {
              recipient: '{{payload.recipient}}',
              content: 'Your payment intent {{steps.create-payment.paymentId}} was created.',
              channel: '{{payload.channel}}'
            }
          }
        ]
      },
      null,
      2
    ),
  },
];
