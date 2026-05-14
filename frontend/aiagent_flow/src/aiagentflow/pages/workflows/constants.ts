import type { AiAgentNodeConfig } from './types';

export const ALLOWED_ACTIVITY_TYPES = [
  'ai.agent',
  'channel.send',
  'connect.send_whatsapp_template',
  'connect.update_inbox_status',
  'connect.enqueue_campaign_message',
  'human.assign',
  'human.handoff',
  'kyc.document_check',
  'kyc.review_case',
  'payments.create_intent',
  'http.request',
  'webhook.call',
  'files.read',
  'drive.lookup',
  'storage.write',
  'mcp.tool_call',
  'voice.call',
  'callcenter.outbound_call',
] as const;

export const WORKFLOW_DESIGN_TYPES = ['workflow', 'tool'] as const;
export type WorkflowDesignType = (typeof WORKFLOW_DESIGN_TYPES)[number];

export const TOOL_ACTIVITY_TYPES = [
  'ai.agent',
  'human.assign',
  'human.handoff',
  'kyc.document_check',
  'kyc.review_case',
  'payments.create_intent',
] as const;

export const ACTIVITY_TYPE_LABELS_ES: Record<string, string> = {
  'ai.agent': 'Agente de IA',
  'channel.send': 'Enviar por canal',
  'connect.send_whatsapp_template': 'Enviar plantilla de WhatsApp',
  'connect.update_inbox_status': 'Actualizar estado de conversacion',
  'connect.enqueue_campaign_message': 'Encolar mensaje de campana',
  'human.assign': 'Asignar a agente',
  'human.handoff': 'Escalar a atencion humana',
  'kyc.document_check': 'Validacion de documento KYC',
  'kyc.review_case': 'Revision humana KYC',
  'payments.create_intent': 'Crear intencion de pago',
  'http.request': 'Consultar API',
  'webhook.call': 'Llamar webhook',
  'files.read': 'Leer archivo',
  'drive.lookup': 'Buscar en Drive',
  'storage.write': 'Guardar en storage',
  'mcp.tool_call': 'Usar herramienta MCP',
  'voice.call': 'Llamada de voz',
  'callcenter.outbound_call': 'Llamada call center',
};

export const ACTIVITY_TYPE_CATEGORY_ES: Record<string, string> = {
  ai: 'IA',
  channel: 'Canales',
  connect: 'Conexiones',
  human: 'Atencion Humana',
  kyc: 'Identidad (KYC)',
  payments: 'Pagos',
  http: 'Datos',
  webhook: 'Datos',
  files: 'Archivos',
  drive: 'Archivos',
  storage: 'Archivos',
  mcp: 'MCP',
  voice: 'Voz',
  callcenter: 'Call Center',
  other: 'Otros',
};

export function activityTypeLabel(type: string): string {
  return ACTIVITY_TYPE_LABELS_ES[type] ?? type;
}

export const WORKFLOW_STATUS_LABELS_ES: Record<string, string> = {
  Draft: 'Borrador',
  Published: 'Publicado',
  Archived: 'Archivado',
};

export const EXECUTION_STATUS_LABELS_ES: Record<string, string> = {
  Queued: 'En cola',
  Running: 'En ejecucion',
  Completed: 'Completada',
  Failed: 'Fallida',
};

export function workflowStatusLabel(status: string): string {
  return WORKFLOW_STATUS_LABELS_ES[status] ?? status;
}

export function executionStatusLabel(status: string): string {
  return EXECUTION_STATUS_LABELS_ES[status] ?? status;
}

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
  'human.assign': {
    queue: 'tier1-support',
    priority: 'normal',
  },
  'human.handoff': {
    team: 'support',
    reason: 'needs-human-review',
    priority: 'high',
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
  'http.request': {
    method: 'GET',
    url: '',
    body: '',
    authProfileId: '',
  },
  'webhook.call': {
    url: '',
    body: '{{payload}}',
    authProfileId: '',
  },
  'files.read': {
    source: 'excel',
    path: '',
    query: '{{payload.query}}',
  },
  'drive.lookup': {
    folder: '',
    query: '{{payload.query}}',
  },
  'storage.write': {
    bucket: '',
    path: '',
    content: '{{steps.agent.result}}',
  },
  'mcp.tool_call': {
    server: '',
    tool: '',
    input: '{{payload}}',
  },
  'voice.call': {
    provider: 'twilio',
    phoneNumber: '{{payload.phone}}',
    script: '{{steps.agent.result}}',
  },
  'callcenter.outbound_call': {
    campaignId: '{{payload.campaignId}}',
    phoneNumber: '{{payload.phone}}',
    script: '{{steps.agent.result}}',
  },
  'ai.agent': {
    agentId: '',
    agentName: '',
    agentVersion: '',
    input: '{{payload.content}}',
    context: '{{payload.channel}}',
  },
};

export const DEFAULT_AI_AGENT_CONFIG: AiAgentNodeConfig = {
  agentId: '',
  agentName: '',
  agentVersion: '',
  model: 'gpt-4o',
  instructions: 'Responde al cliente de forma clara, breve y segura usando el contexto disponible.',
  tools: [],
  context: '',
  knowledge: [],
  input: '{{payload.content}}',
  outputVariable: 'agentResult',
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
    name: 'Enrutamiento automatico de Inbox + Acuse',
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
    name: 'Flujo KYC de validacion documental',
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
    name: 'Intencion de pago + seguimiento',
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

