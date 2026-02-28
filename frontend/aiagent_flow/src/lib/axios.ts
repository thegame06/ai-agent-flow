import type { AxiosRequestConfig } from 'axios';

import axios from 'axios';

import { CONFIG } from 'src/global-config';

// ----------------------------------------------------------------------

const axiosInstance = axios.create({ baseURL: CONFIG.serverUrl });

axiosInstance.interceptors.response.use(
  (response) => response,
  (error) => Promise.reject((error.response && error.response.data) || 'Something went wrong!')
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
    // Agents
    agents: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/agents`,
      detail: (tenantId: string, agentId: string) => `/api/v1/tenants/${tenantId}/agents/${agentId}`,
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
    },
    // Evaluations
    evaluations: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/evaluations`,
      create: (tenantId: string) => `/api/v1/tenants/${tenantId}/evaluations`,
      detail: (tenantId: string, runId: string) => `/api/v1/tenants/${tenantId}/evaluations/${runId}`,
    },
    // Feature Flags
    featureFlags: {
      list: (tenantId: string) => `/api/v1/tenants/${tenantId}/feature-flags`,
      toggle: (tenantId: string, flagKey: string) => `/api/v1/tenants/${tenantId}/feature-flags/${flagKey}/toggle`,
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
