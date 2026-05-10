import { createAsyncThunk } from '@reduxjs/toolkit';

import { workflowStudioApi } from '../services/workflowStudioApi';


type RuntimePayload = {
  workflows: any[];
  executions: any[];
  metrics: any;
  auditEvents: any[];
  activityCatalog: any[];
  availableModels: any[];
  availableTools: any[];
  integrations: any[];
  connectTemplates: any[];
};

export const fetchWorkflowRuntimeData = createAsyncThunk(
  'workflowRuntime/fetchRuntimeData',
  async (tenantId: string): Promise<RuntimePayload> => {
    const [wfRes, exRes, metRes, auditRes, catalogRes, modelsRes, toolsRes, integrationsRes, templatesRes] =
      await Promise.all([
        workflowStudioApi.getDefinitions(tenantId),
        workflowStudioApi.getExecutions(tenantId),
        workflowStudioApi.getMetrics(tenantId),
        workflowStudioApi.getAuditEvents(tenantId),
        workflowStudioApi.getCatalogActivities(tenantId),
        workflowStudioApi.getModels(),
        workflowStudioApi.getTools(),
        workflowStudioApi.getIntegrationStatus(tenantId),
        workflowStudioApi.getConnectTemplates(tenantId),
      ]);
    const integrations = Array.isArray(integrationsRes.data) ? integrationsRes.data : [];

    return {
      workflows: wfRes.data ?? [],
      executions: exRes.data ?? [],
      metrics: metRes.data ?? null,
      auditEvents: auditRes.data ?? [],
      activityCatalog: catalogRes.data ?? [],
      availableModels: Array.isArray(modelsRes.data) ? modelsRes.data : [],
      availableTools: Array.isArray(toolsRes.data) ? toolsRes.data : [],
      integrations,
      connectTemplates: Array.isArray(templatesRes.data) ? templatesRes.data : [],
    };
  }
);

export const saveWorkflowDraft = createAsyncThunk(
  'workflowRuntime/saveWorkflowDraft',
  async ({
    tenantId,
    workflow,
  }: {
    tenantId: string;
    workflow: {
      id: string;
      name: string;
      triggerEventName: string;
      definitionJson: string;
    };
  }) => {
    JSON.parse(workflow.definitionJson);
    await workflowStudioApi.upsertDefinition(tenantId, workflow.id, {
      name: workflow.name.trim(),
      triggerEventName: workflow.triggerEventName.trim(),
      definitionJson: workflow.definitionJson,
      metadata: {
        designType: (workflow as any).designType ?? 'workflow',
      },
    });
    return workflow;
  }
);

export const publishWorkflowDefinition = createAsyncThunk(
  'workflowRuntime/publishWorkflowDefinition',
  async ({
    tenantId,
    workflowId,
  }: {
    tenantId: string;
    workflowId: string;
  }) => {
    await workflowStudioApi.publishDefinition(tenantId, workflowId);
    return workflowId;
  }
);

export const runWorkflowEvent = createAsyncThunk(
  'workflowRuntime/runWorkflowEvent',
  async ({
    tenantId,
    eventName,
  }: {
    tenantId: string;
    eventName: string;
  }) => {
    await workflowStudioApi.runEvent(tenantId, {
      eventName: eventName || 'connect.message.received',
      payload: {
        channel: 'whatsapp',
        recipient: '5215555555555',
        customerName: 'Demo User',
        content: 'Hola desde Studio Workflows',
      },
    });
    return true;
  }
);

export const retryWorkflowExecution = createAsyncThunk(
  'workflowRuntime/retryWorkflowExecution',
  async ({
    tenantId,
    executionId,
  }: {
    tenantId: string;
    executionId: string;
  }) => {
    await workflowStudioApi.retryExecution(tenantId, executionId);
    return executionId;
  }
);

export const fetchWorkflowExecutionSteps = createAsyncThunk(
  'workflowRuntime/fetchWorkflowExecutionSteps',
  async ({
    tenantId,
    executionId,
  }: {
    tenantId: string;
    executionId: string;
  }) => {
    const res = await workflowStudioApi.getExecutionSteps(tenantId, executionId);
    return res.data ?? [];
  }
);
