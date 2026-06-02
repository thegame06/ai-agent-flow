import type { AxiosRequestConfig } from 'axios';

import axios from 'axios';

import { CONFIG } from 'src/global-config';

// ----------------------------------------------------------------------

const axiosInstance = axios.create({ 
  baseURL: CONFIG.serverUrl,
  timeout: 30000, // 30 segundos timeout
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor - para agregar auth token si existe
axiosInstance.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('auth_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => {
    console.error('Request error:', error);
    return Promise.reject(error);
  }
);

// Response interceptor - para manejar errores globalmente
axiosInstance.interceptors.response.use(
  (response) => response,
  (error) => {
    // Logging detallado para debugging
    console.error('API Error:', {
      url: error.config?.url,
      method: error.config?.method,
      status: error.response?.status,
      data: error.response?.data,
      message: error.message,
    });

    // Manejo de errores por código HTTP
    if (error.response?.status === 401) {
      // Opcional: Redirect a login si es necesario
      console.warn('Unauthorized - Consider implementing auth redirect');
    } else if (error.response?.status === 404) {
      console.warn('Resource not found:', error.config?.url);
    } else if (error.response?.status >= 500) {
      console.error('Server error - Backend may be down');
    }

    // Retornar error con información estructurada
    const errorMessage = error.response?.data?.message 
      || error.response?.data?.error 
      || error.message 
      || 'Something went wrong!';

    return Promise.reject({
      ...error,
      message: errorMessage,
      statusCode: error.response?.status,
      data: error.response?.data,
    });
  }
);

export default axiosInstance;

// ----------------------------------------------------------------------

export const fetcher = async (args: string | [string, AxiosRequestConfig]) => {
  try {
    const [url, config] = Array.isArray(args) ? args : [args];

    const res = await axiosInstance.get(url, { ...config });

    return res.data;
  } catch (error) {
    console.error('Failed to fetch:', error);
    throw error;
  }
};

// ----------------------------------------------------------------------

export const endpoints = {
  // ─────────────────────────────────────────────
  // AgentFlow API Endpoints (Multi-tenant)
  // ─────────────────────────────────────────────
  agentflow: {
    assistant: {
      wizardMetrics: (tenantId: string) => `/api/v1/assistant/wizard/metrics?tenantId=${encodeURIComponent(tenantId)}`,
      wizardCreateSession: '/api/v1/assistant/wizard/sessions',
      wizardSession: (sessionId: string) => `/api/v1/assistant/wizard/sessions/${encodeURIComponent(sessionId)}`,
      wizardAnswer: (sessionId: string) => `/api/v1/assistant/wizard/sessions/${encodeURIComponent(sessionId)}/answers`,
      wizardMaterialize: (sessionId: string) => `/api/v1/assistant/wizard/sessions/${encodeURIComponent(sessionId)}/materialize`,
    },
    channels: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/channels`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/channels`,
      activate: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/activate`,
      deactivate: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/deactivate`,
      delete: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}`,
      status: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/status`,
      qr: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/qr`,
      routingGet: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/routing`,
      routingUpdate: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/routing`,
      routingPreview: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/routing/preview`,
      intentsCatalog: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/intents/catalog`,
      intentsApply: (tenantId: string, channelId: string) => `/api/v1/tenants/${tenantId}/channels/${channelId}/intents/apply`,
    },
    connections: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/connections`,
      upsert: (tenantId: string, connectionId: string) => `/api/v1/tenants/${tenantId}/connections/${connectionId}`,
      secret: (tenantId: string, connectionId: string) => `/api/v1/tenants/${tenantId}/connections/${connectionId}/secret`,
      health: (tenantId: string, connectionId: string) => `/api/v1/tenants/${tenantId}/connections/${connectionId}/health`,
      resources: (tenantId: string) => `/api/v1/tenants/${tenantId}/connections/resources`,
    },
    // Agents
    agents: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/agents`,
      detail: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}`,
      toolCatalog: (tenantId: string) => `/api/v1/tenants/${tenantId}/agents/tool-catalog`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/agents`,
      update: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}`,
      delete: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}`,
      clone: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/clone`,
      publish: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/publish`,
      archive: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/archive`,
    },
    // Executions
    executions: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/executions`,
      byAgent: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/executions`,
      detail: (tenantId: string, agentId: string, executionId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/executions/${executionId}`,
      trigger: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/trigger`,
      handoffAllowedTargets: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/handoff/allowed-targets`,
      handoffDecision: (tenantId: string, agentId: string, targetAgentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}/handoff/decision?targetAgentId=${encodeURIComponent(targetAgentId)}`,
    },
    // Checkpoints (HITL)
    checkpoints: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/checkpoints`,
      decide: (tenantId: string, executionId: string) => `/api/v1/tenants/${tenantId}/checkpoints/${executionId}/decide`,
    },
    inbox: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/inbox`,
      detail: (tenantId: string, conversationId: string) => `/api/v1/tenants/${tenantId}/inbox/${encodeURIComponent(conversationId)}`,
      stats: (tenantId: string) => `/api/v1/tenants/${tenantId}/inbox/stats`,
    },
    campaigns: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaigns`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaigns`,
      detail: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}`,
      update: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}`,
      publish: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/publish`,
      pause: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/pause`,
      resume: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/resume`,
      simulate: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/simulate`,
      runNow: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/run-now`,
      runs: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/runs`,
      metrics: (tenantId: string, campaignId: string) => `/api/v1/tenants/${tenantId}/campaigns/${encodeURIComponent(campaignId)}/metrics`,
      allRuns: (tenantId: string, campaignId?: string | null, limit = 100) =>
        `/api/v1/tenants/${tenantId}/campaign-runs?campaignId=${encodeURIComponent(campaignId ?? '')}&limit=${limit}`,
      runDetail: (tenantId: string, runId: string) => `/api/v1/tenants/${tenantId}/campaign-runs/${encodeURIComponent(runId)}`,
      retryFailures: (tenantId: string, runId: string) => `/api/v1/tenants/${tenantId}/campaign-runs/${encodeURIComponent(runId)}/retry-failures`,
      contactResults: (tenantId: string, runId: string) => `/api/v1/tenants/${tenantId}/campaign-runs/${encodeURIComponent(runId)}/contacts`,
    },
    campaignSegments: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaign-segments`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaign-segments`,
      detail: (tenantId: string, segmentId: string) => `/api/v1/tenants/${tenantId}/campaign-segments/${encodeURIComponent(segmentId)}`,
      update: (tenantId: string, segmentId: string) => `/api/v1/tenants/${tenantId}/campaign-segments/${encodeURIComponent(segmentId)}`,
      preview: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaign-segments/preview`,
      previewById: (tenantId: string, segmentId: string) => `/api/v1/tenants/${tenantId}/campaign-segments/${encodeURIComponent(segmentId)}/preview`,
    },
    campaignBuilder: {
      draftFromPrompt: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaign-builder/draft-from-prompt`,
      refine: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaign-builder/refine`,
      validate: (tenantId: string) => `/api/v1/tenants/${tenantId}/campaign-builder/validate`,
    },
    // Tools / Extensions
    extensions: {
      tools: '/api/v1/extensions/tools',
      catalog: '/api/v1/extensions/catalog',
      invoke: (toolName: string) => `/api/v1/extensions/tools/${toolName}/invoke`,
    },
    // Model Routing
    models: {
      list: '/api/v1/model-routing/models',
      healthy: '/api/v1/model-routing/models/healthy',
    },
    runtimeModelProfiles: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/runtime-model-profiles`,
      byId: (tenantId: string, profileId: string) => `/api/v1/tenants/${tenantId}/runtime-model-profiles/${profileId}`,
    },
    testStudio: {
      listSessions: (tenantId: string, runtimeKind: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions`,
      createSession: (tenantId: string, runtimeKind: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions`,
      sendMessage: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/messages`,
      timeline: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/timeline`,
      transcript: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/transcript`,
      metrics: (tenantId: string, runtimeKind: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/metrics`,
      close: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/close`,
      updateCorrelation: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/correlation`,
      registerAttachment: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/attachments`,
      uploadAttachment: (tenantId: string, runtimeKind: string, sessionId: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/attachments/upload`,
      downloadAttachment: (tenantId: string, runtimeKind: string, sessionId: string, attachmentRef: string) =>
        `/api/v1/tenants/${tenantId}/test-studio/${encodeURIComponent(runtimeKind)}/sessions/${encodeURIComponent(sessionId)}/attachments/${encodeURIComponent(attachmentRef)}/download`,
    },
    // Policies
    policies: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/policies`,
      detail: (tenantId: string, policyId: string) => `/api/v1/tenants/${tenantId}/policies/${policyId}`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/policies`,
      update: (tenantId: string, policyId: string) => `/api/v1/tenants/${tenantId}/policies/${policyId}`,
      delete: (tenantId: string, policyId: string) => `/api/v1/tenants/${tenantId}/policies/${policyId}`,
    },
    // Audit
    audit: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/audit`,
      correlations: (tenantId: string) => `/api/v1/tenants/${tenantId}/audit/correlations`,
      journey: (tenantId: string, correlationId: string) => `/api/v1/tenants/${tenantId}/audit/journey/${encodeURIComponent(correlationId)}`,
      operationsSummary: (tenantId: string) => `/api/v1/tenants/${tenantId}/audit/operations/summary`,
      deadLetters: (tenantId: string) => `/api/v1/tenants/${tenantId}/audit/operations/deadletters`,
      replayDeadLetter: (tenantId: string, deadLetterId: string) => `/api/v1/tenants/${tenantId}/audit/operations/deadletters/${deadLetterId}/replay`,
    },
    // Evaluations
    evaluations: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/evaluations`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/evaluations`,
      detail: (tenantId: string, runId: string) => `/api/v1/tenants/${tenantId}/evaluations/${runId}`,
      byExecution: (tenantId: string, executionId: string) => `/api/v1/tenants/${tenantId}/evaluations/executions/${executionId}`,
      byAgent: (tenantId: string, agentKey: string) => `/api/v1/tenants/${tenantId}/evaluations/agents/${agentKey}`,
      agentSummary: (tenantId: string, agentKey: string) => `/api/v1/tenants/${tenantId}/evaluations/agents/${agentKey}/summary`,
      pendingReview: (tenantId: string) => `/api/v1/tenants/${tenantId}/evaluations/pending-review`,
    },
    // Feature Flags
    featureFlags: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/feature-flags`,
      check: (tenantId: string, flagKey: string) => `/api/v1/tenants/${tenantId}/feature-flags/${flagKey}/check`,
      enabled: (tenantId: string) => `/api/v1/tenants/${tenantId}/feature-flags/enabled`,
      update: (tenantId: string, flagKey: string) => `/api/v1/tenants/${tenantId}/feature-flags/${flagKey}`,
    },
    // Conversation Threads
    threads: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/threads`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/threads`,
      detail: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/threads/${threadId}`,
      metrics: (tenantId: string) => `/api/v1/tenants/${tenantId}/threads/metrics`,
      history: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/threads/${threadId}/history`,
      sendMessage: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/threads/${threadId}/messages`,
      updateInbox: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/threads/${threadId}/inbox`,
      archive: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/threads/${threadId}/archive`,
      delete: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/threads/${threadId}`,
    },
    channelSessions: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/channel-sessions`,
      detail: (tenantId: string, sessionId: string) => `/api/v1/tenants/${tenantId}/channel-sessions/${sessionId}`,
      messages: (tenantId: string, sessionId: string) => `/api/v1/tenants/${tenantId}/channel-sessions/${sessionId}/messages`,
    },
    commerce: {
      resolveParty: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/crm/resolve-party`,
      partyByIdentity: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/crm/party-by-identity`,
      customers: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/crm/customers`,
      customerById: (tenantId: string, partyId: string) => `/api/v1/tenants/${tenantId}/commerce/crm/customers/${partyId}`,
      contextBySession: (tenantId: string, sessionId: string) => `/api/v1/tenants/${tenantId}/commerce/conversation-context/${sessionId}`,
      contextByThread: (tenantId: string, threadId: string) => `/api/v1/tenants/${tenantId}/commerce/conversation-context/by-thread/${threadId}`,
      sendConversationMessage: (tenantId: string, sessionId: string) => `/api/v1/tenants/${tenantId}/commerce/conversation-context/${sessionId}/messages`,
      closeConversation: (tenantId: string, sessionId: string) => `/api/v1/tenants/${tenantId}/commerce/conversation-context/${sessionId}/close`,
      inventorySearch: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/search`,
      inventoryItemBySku: (tenantId: string, sku: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/items/${sku}`,
      inventoryAdjust: (tenantId: string, sku: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/items/${sku}/adjust`,
      inventoryMovements: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/movements`,
      categories: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/categories`,
      categoryById: (tenantId: string, categoryId: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/categories/${categoryId}`,
      branches: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/branches`,
      branchById: (tenantId: string, branchId: string) => `/api/v1/tenants/${tenantId}/commerce/inventory/branches/${branchId}`,
      salesSearch: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/sales`,
      saleById: (tenantId: string, saleId: string) => `/api/v1/tenants/${tenantId}/commerce/sales/${saleId}`,
      updateSale: (tenantId: string, saleId: string) => `/api/v1/tenants/${tenantId}/commerce/sales/${saleId}`,
      calculateSale: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/sales/calculate`,
      createSale: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/sales`,
      createOrder: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/orders`,
      ordersSearch: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/orders`,
      orderById: (tenantId: string, orderId: string) => `/api/v1/tenants/${tenantId}/commerce/orders/${orderId}`,
      invoicesSearch: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices`,
      createInvoice: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices`,
      invoiceById: (tenantId: string, invoiceId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices/${invoiceId}`,
      updateInvoice: (tenantId: string, invoiceId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices/${invoiceId}`,
      invoiceStatus: (tenantId: string, invoiceId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices/${invoiceId}/status`,
      invoicePdf: (tenantId: string, invoiceId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices/${invoiceId}/pdf`,
      invoiceSendWhatsApp: (tenantId: string, invoiceId: string) => `/api/v1/tenants/${tenantId}/commerce/billing/invoices/${invoiceId}/send-whatsapp`,
      storeSettings: (tenantId: string) => `/api/v1/tenants/${tenantId}/commerce/store/settings`,
    },
    // Workflow Studio / Control
    workflows: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/studio/workflows`,
      detail: (tenantId: string, workflowId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/${workflowId}`,
      upsert: (tenantId: string, workflowId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/${workflowId}`,
      publish: (tenantId: string, workflowId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/${workflowId}/publish`,
      catalogActivities: (tenantId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/catalog/activities`,
      runEvent: (tenantId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/run-event`,
      executions: (tenantId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/executions`,
      steps: (tenantId: string, executionId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/executions/${executionId}/steps`,
      retry: (tenantId: string, executionId: string) => `/api/v1/tenants/${tenantId}/studio/workflows/executions/${executionId}/retry`,
      metrics: (tenantId: string) => `/api/v1/tenants/${tenantId}/control/workflows/metrics`,
      auditEvents: (tenantId: string) => `/api/v1/tenants/${tenantId}/control/workflows/audit/events`,
    },
    kyc: {
      documentCheck: (tenantId: string) => `/api/v1/tenants/${tenantId}/kyc/document-check`,
      review: (tenantId: string, caseId: string) => `/api/v1/tenants/${tenantId}/kyc/review/${caseId}`,
      caseById: (tenantId: string, caseId: string) => `/api/v1/tenants/${tenantId}/kyc/cases/${caseId}`,
      listCases: (tenantId: string) => `/api/v1/tenants/${tenantId}/kyc/cases`,
    },
    transactions: {
      createPayment: (tenantId: string) => `/api/v1/tenants/${tenantId}/transactions/payments`,
      confirmPayment: (tenantId: string, paymentId: string) => `/api/v1/tenants/${tenantId}/transactions/payments/${paymentId}/confirm`,
      listPayments: (tenantId: string) => `/api/v1/tenants/${tenantId}/transactions/payments`,
    },
    // Segment Routing
    segmentRouting: {
      getConfig: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/segment-routing/agents/${agentId}`,
      updateConfig: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/segment-routing/agents/${agentId}`,
      preview: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/segment-routing/agents/${agentId}/preview`,
      disable: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/segment-routing/agents/${agentId}/disable`,
    },
    intentRouting: {
      rules: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/rules`,
      ruleEnable: (tenantId: string, ruleId: string) => `/api/v1/tenants/${tenantId}/intent-routing/rules/${ruleId}/enable`,
      ruleById: (tenantId: string, ruleId: string) => `/api/v1/tenants/${tenantId}/intent-routing/rules/${ruleId}`,
      classify: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/classify`,
      simulate: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/simulate`,
      agents: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/agents`,
      agentById: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/intent-routing/agents/${agentId}`,
      // Conversation Inbox
      conversations: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/conversations`,
      conversationById: (tenantId: string, conversationId: string) => `/api/v1/tenants/${tenantId}/intent-routing/conversations/${conversationId}`,
      conversationReassign: (tenantId: string, conversationId: string) => `/api/v1/tenants/${tenantId}/intent-routing/conversations/${conversationId}/reassign`,
      conversationResolve: (tenantId: string, conversationId: string) => `/api/v1/tenants/${tenantId}/intent-routing/conversations/${conversationId}/resolve`,
      stats: (tenantId: string) => `/api/v1/tenants/${tenantId}/intent-routing/stats`,
    },
    systemOrchestrator: {
      status: (tenantId: string) => `/api/v1/tenants/${tenantId}/system-orchestrator/status`,
    },
    workforce: {
      people: (tenantId: string) => `/api/v1/tenants/${tenantId}/settings/workforce/people`,
      queues: (tenantId: string) => `/api/v1/tenants/${tenantId}/settings/workforce/queues`,
      resolution: (tenantId: string, targetId: string) =>
        `/api/v1/tenants/${tenantId}/settings/workforce/resolution?targetId=${encodeURIComponent(targetId)}`,
    },
    // System
    health: '/health',
  },
  
  // ─────────────────────────────────────────────
  // Demo / Template Endpoints (to be removed)
  // ─────────────────────────────────────────────
  chat: '/api/chat',
  kanban: '/api/kanban',
  calendar: '/api/calendar',
  auth: {
    me: '/api/auth/me',
    signIn: '/api/auth/sign-in',
    signUp: '/api/auth/sign-up',
  },
  mail: {
    list: '/api/mail/list',
    details: '/api/mail/details',
    labels: '/api/mail/labels',
  },
  post: {
    list: '/api/post/list',
    details: '/api/post/details',
    latest: '/api/post/latest',
    search: '/api/post/search',
  },
  product: {
    list: '/api/product/list',
    details: '/api/product/details',
    search: '/api/product/search',
  },
};
