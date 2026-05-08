import { DEFAULT_AI_AGENT_CONFIG } from './constants';

import type { SchemaFieldRule, WorkflowActivityNode } from './types';

export function parseSchemaFieldRule(key: string, descriptor: string): SchemaFieldRule {
  const normalized = (descriptor || '').toLowerCase();
  const required = !normalized.includes('optional') && !normalized.includes('?');

  let defaultValue: string | undefined;
  const defaultMatch = descriptor.match(/default\s*[:=]\s*([^;|,]+)/i);
  if (defaultMatch?.[1]) defaultValue = defaultMatch[1].trim();

  return { key, required, defaultValue };
}

export function validateWorkflow(
  activities: WorkflowActivityNode[],
  triggerEventName: string,
  allowedTypes: string[],
  requiredConfigByType: Record<string, string[]>
): string[] {
  const errors: string[] = [];
  if (!triggerEventName.trim()) errors.push('Trigger event is required.');
  if (activities.length === 0) errors.push('At least one activity is required.');
  if (activities.length > 100) errors.push('Workflow exceeds max activities (100).');

  const ids = new Set<string>();
  activities.forEach((activity, idx) => {
    const step = idx + 1;
    if (!activity.id?.trim()) errors.push(`Step ${step}: id is required.`);
    if (activity.id && ids.has(activity.id)) errors.push(`Step ${step}: duplicate id '${activity.id}'.`);
    if (activity.id) ids.add(activity.id);

    if (!allowedTypes.includes(activity.type)) {
      errors.push(`Step ${step}: activity type '${activity.type}' is not allowed.`);
    }

    const timeoutMs = activity.timeoutMs ?? 30000;
    const retryCount = activity.retryCount ?? 0;
    const retryDelayMs = activity.retryDelayMs ?? 0;

    if (timeoutMs < 1 || timeoutMs > 120000) errors.push(`Step ${step}: timeout must be 1..120000.`);
    if (retryCount < 0 || retryCount > 5) errors.push(`Step ${step}: retry must be 0..5.`);
    if (retryDelayMs < 0 || retryDelayMs > 30000) errors.push(`Step ${step}: delay must be 0..30000.`);

    const cfg = activity.config ?? {};
    if (activity.type === 'ai.agent') {
      const ai = activity.aiAgent ?? DEFAULT_AI_AGENT_CONFIG;
      if (!ai.model.trim()) errors.push(`Step ${step}: AI model is required.`);
      if (!ai.instructions.trim()) errors.push(`Step ${step}: AI instructions are required.`);
      if (ai.maxTokens < 64 || ai.maxTokens > 8000) errors.push(`Step ${step}: maxTokens must be 64..8000.`);
      if (ai.temperature < 0 || ai.temperature > 1) errors.push(`Step ${step}: temperature must be 0..1.`);
    }

    const requiredConfig = requiredConfigByType[activity.type] ?? [];
    requiredConfig.forEach((key) => {
      if (!cfg[key]?.trim()) errors.push(`Step ${step}: ${key} is required.`);
    });
  });

  return errors;
}
