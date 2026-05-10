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
  if (!triggerEventName.trim()) errors.push('El evento disparador es requerido.');
  if (activities.length === 0) errors.push('Agrega al menos un nodo.');
  if (activities.length > 100) errors.push('El workflow excede el maximo de 100 nodos.');

  const ids = new Set<string>();
  activities.forEach((activity, idx) => {
    const step = idx + 1;
    if (!activity.id?.trim()) errors.push(`Nodo ${step}: el ID interno es requerido.`);
    if (activity.id && ids.has(activity.id)) errors.push(`Nodo ${step}: el ID '${activity.id}' esta duplicado.`);
    if (activity.id) ids.add(activity.id);

    if (!allowedTypes.includes(activity.type)) {
      errors.push(`Nodo ${step}: el tipo '${activity.type}' no esta permitido.`);
    }

    const timeoutMs = activity.timeoutMs ?? 30000;
    const retryCount = activity.retryCount ?? 0;
    const retryDelayMs = activity.retryDelayMs ?? 0;

    if (timeoutMs < 1 || timeoutMs > 120000) errors.push(`Nodo ${step}: timeout debe estar entre 1 y 120000.`);
    if (retryCount < 0 || retryCount > 5) errors.push(`Nodo ${step}: reintentos debe estar entre 0 y 5.`);
    if (retryDelayMs < 0 || retryDelayMs > 30000) errors.push(`Nodo ${step}: delay debe estar entre 0 y 30000.`);

    const cfg = activity.config ?? {};
    if (activity.type === 'ai.agent') {
      const ai = activity.aiAgent ?? DEFAULT_AI_AGENT_CONFIG;
      if (!cfg.agentId?.trim() && !ai.agentId?.trim()) errors.push(`Nodo ${step}: selecciona un agente publicado.`);
      if (!cfg.input?.trim() && !ai.input?.trim()) errors.push(`Nodo ${step}: define el mensaje o contexto de entrada para el agente.`);
      if (ai.maxTokens < 64 || ai.maxTokens > 8000) errors.push(`Nodo ${step}: tokens maximos debe estar entre 64 y 8000.`);
      if (ai.temperature < 0 || ai.temperature > 1) errors.push(`Nodo ${step}: temperatura debe estar entre 0 y 1.`);
    }

    const requiredConfig = requiredConfigByType[activity.type] ?? [];
    requiredConfig.forEach((key) => {
      if (!cfg[key]?.trim()) errors.push(`Nodo ${step}: completa el campo ${key}.`);
    });
  });

  return errors;
}
