import axios, { endpoints } from 'src/lib/axios';

export const workflowStudioApi = {
  getDefinitions: (tenantId: string) => axios.get(endpoints.agentflow.workflows.list(tenantId)),
  getExecutions: (tenantId: string) => axios.get(endpoints.agentflow.workflows.executions(tenantId)),
  getMetrics: (tenantId: string, window: '24h' | '7d' | '30d' = '24h') =>
    axios.get(endpoints.agentflow.workflows.metrics(tenantId), { params: { window } }),
  getWizardMetrics: (tenantId: string) => axios.get(endpoints.agentflow.assistant.wizardMetrics(tenantId)),
  getAuditEvents: (tenantId: string) => axios.get(endpoints.agentflow.workflows.auditEvents(tenantId)),
  getCatalogActivities: (tenantId: string) =>
    axios.get(endpoints.agentflow.workflows.catalogActivities(tenantId)).catch(() => ({ data: [] })),
  getModels: () => axios.get('/api/v1/model-routing/models').catch(() => ({ data: [] })),
  getTools: () => axios.get('/api/v1/extensions/tools').catch(() => ({ data: [] })),
  getConnectTemplates: (tenantId: string) =>
    axios.get(`/api/v1/tenants/${tenantId}/connect/templates`).catch(() => ({ data: [] })),
  getAgents: (tenantId: string) =>
    axios.get(endpoints.agentflow.agents.list(tenantId)).catch(() => ({ data: [] })),
  getChannels: (tenantId: string) =>
    axios.get(endpoints.agentflow.channels.list(tenantId)).catch(() => ({ data: [] })),
  getIntegrationStatus: (tenantId: string) =>
    axios.get(`${endpoints.agentflow.workflows.list(tenantId)}/integrations/status`).catch(() => ({ data: [] })),
  upsertDefinition: (tenantId: string, workflowId: string, body: unknown) =>
    axios.put(endpoints.agentflow.workflows.upsert(tenantId, workflowId), body),
  publishDefinition: (tenantId: string, workflowId: string) =>
    axios.post(endpoints.agentflow.workflows.publish(tenantId, workflowId)),
  runEvent: (tenantId: string, body: unknown) =>
    axios.post(endpoints.agentflow.workflows.runEvent(tenantId), body),
  retryExecution: (tenantId: string, executionId: string) =>
    axios.post(endpoints.agentflow.workflows.retry(tenantId, executionId)),
  getExecutionSteps: (tenantId: string, executionId: string) =>
    axios.get(endpoints.agentflow.workflows.steps(tenantId, executionId)),
};
