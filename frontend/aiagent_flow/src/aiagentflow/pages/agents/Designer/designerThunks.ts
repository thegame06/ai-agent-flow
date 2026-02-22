import { createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

import type { AgentDefinitionDraft } from './types';

const TENANT_ID = 'tenant-1'; // TODO: Get from auth context

// ── Fetch single agent for Designer ──
export const fetchAgentDetail = createAsyncThunk(
  'designer/fetchAgentDetail',
  async (agentId: string) => {
    const response = await axios.get(`/api/v1/tenants/${TENANT_ID}/agents/${agentId}`);
    return response.data;
  }
);

// ── Save agent (Create or Update) ──
export const saveAgent = createAsyncThunk(
  'designer/saveAgent',
  async (draft: AgentDefinitionDraft) => {
    const payload = mapDraftToPayload(draft);

    if (draft.id) {
      // UPDATE
      const response = await axios.put(
        `/api/v1/tenants/${TENANT_ID}/agents/${draft.id}`,
        payload
      );
      return response.data;
    }
    // CREATE
    const response = await axios.post(
      `/api/v1/tenants/${TENANT_ID}/agents`,
      payload
    );
    return response.data;
  }
);

// ── Publish agent ──
export const publishAgent = createAsyncThunk(
  'designer/publishAgent',
  async (agentId: string) => {
    const response = await axios.post(
      `/api/v1/tenants/${TENANT_ID}/agents/${agentId}/publish`
    );
    return response.data;
  }
);

// ── Delete agent ──
export const deleteAgent = createAsyncThunk(
  'designer/deleteAgent',
  async (agentId: string) => {
    await axios.delete(`/api/v1/tenants/${TENANT_ID}/agents/${agentId}`);
    return agentId;
  }
);

// ── Clone agent ──
export const cloneAgent = createAsyncThunk(
  'designer/cloneAgent',
  async ({ agentId, newName, newDescription }: { agentId: string; newName: string; newDescription?: string }) => {
    const response = await axios.post(
      `/api/v1/tenants/${TENANT_ID}/agents/${agentId}/clone`,
      { newName, newDescription }
    );
    return response.data;
  }
);

// ── Preview agent (dry-run execution) ──
export const previewAgent = createAsyncThunk(
  'designer/previewAgent',
  async ({ agentId, message, variables }: { agentId: string; message: string; variables?: Record<string, string> }) => {
    const response = await axios.post(
      `/api/v1/tenants/${TENANT_ID}/agents/${agentId}/preview`,
      { message, variables }
    );
    return response.data;
  }
);

// ── Map frontend draft → backend DTO ──
function mapDraftToPayload(draft: AgentDefinitionDraft) {
  return {
    name: draft.name,
    description: draft.description,
    version: draft.version,
    status: draft.status,
    brain: {
      primaryModel: draft.model.primaryModel,
      fallbackModel: draft.model.fallbackModel,
      provider: 'OpenAI', // Default; could be extended
      systemPrompt: draft.systemPrompt,
      temperature: draft.model.temperature,
      maxResponseTokens: draft.model.maxResponseTokens,
    },
    loop: {
      maxSteps: draft.guardrails.maxSteps,
      timeoutPerStepMs: draft.guardrails.timeoutPerStepMs,
      maxTokensPerExecution: draft.guardrails.maxTokensPerExecution,
      maxRetries: draft.guardrails.maxRetries,
      enablePromptInjectionGuard: draft.guardrails.enablePromptInjectionGuard,
      enablePIIProtection: draft.guardrails.enablePIIProtection,
      requireHumanApproval: draft.guardrails.hitl.enabled,
      humanApprovalThreshold: draft.guardrails.hitl.requireReviewOnAllToolCalls ? 'always' : 'high_risk',
    },
    memory: {
      workingMemory: draft.memory.workingMemory,
      longTermMemory: draft.memory.longTermMemory,
      vectorMemory: draft.memory.vectorMemory,
      auditMemory: draft.memory.auditMemory,
    },
    steps: draft.steps.map((s) => ({
      id: s.id,
      type: s.type,
      label: s.label,
      description: s.description,
      config: s.config,
      position: s.position,
      connections: s.connections,
    })),
    tools: draft.tools.map((t) => ({
      toolId: t.toolId,
      toolName: t.toolName,
      version: t.version,
      riskLevel: t.riskLevel,
      permissions: t.permissions,
    })),
    tags: draft.tags,
  };
}

// ── Map backend response → frontend draft ──
export function mapResponseToDraft(data: Record<string, unknown>): AgentDefinitionDraft {
  const brain = (data.brain ?? {}) as Record<string, unknown>;
  const loop = (data.loop ?? {}) as Record<string, unknown>;
  const memory = (data.memory ?? {}) as Record<string, unknown>;

  return {
    id: data.id as string,
    name: (data.name as string) ?? '',
    description: (data.description as string) ?? '',
    version: String(data.version ?? '1.0.0'),
    status: (data.status as 'Draft' | 'Published' | 'Archived') ?? 'Draft',
    systemPrompt: (brain.systemPrompt as string) ?? '',
    tags: (data.tags as string[]) ?? [],
    model: {
      primaryModel: (brain.primaryModel as string) ?? 'gpt-4o',
      fallbackModel: (brain.fallbackModel as string) ?? 'gpt-4o-mini',
      temperature: (brain.temperature as number) ?? 0.7,
      maxResponseTokens: (brain.maxResponseTokens as number) ?? 4096,
    },
    guardrails: {
      maxSteps: (loop.maxSteps as number) ?? 25,
      timeoutPerStepMs: (loop.timeoutPerStepMs as number) ?? 30000,
      maxTokensPerExecution: (loop.maxTokensPerExecution as number) ?? 100000,
      maxRetries: (loop.maxRetries as number) ?? 3,
      enablePromptInjectionGuard: (loop.enablePromptInjectionGuard as boolean) ?? true,
      enablePIIProtection: (loop.enablePIIProtection as boolean) ?? true,
      hitl: {
        enabled: (loop.requireHumanApproval as boolean) ?? false,
        requireReviewOnAllToolCalls: (loop.humanApprovalThreshold as string) === 'always',
        requireReviewOnPolicyEscalation: true,
        confidenceThreshold: 0.7,
      },
    },
    memory: {
      workingMemory: (memory.workingMemory as boolean) ?? true,
      longTermMemory: (memory.longTermMemory as boolean) ?? false,
      vectorMemory: (memory.vectorMemory as boolean) ?? false,
      auditMemory: true, // Always true
    },
    steps: (data.steps as AgentDefinitionDraft['steps']) ?? [],
    tools: (data.tools as AgentDefinitionDraft['tools']) ?? [],
  };
}
