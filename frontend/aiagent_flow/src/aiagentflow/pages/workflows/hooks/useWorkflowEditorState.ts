import { useMemo, useState } from 'react';

import { useAppDispatch, useAppSelector } from 'src/aiagentflow/store/hooks';

import { validateWorkflow, parseSchemaFieldRule } from '../validation';
import {
  ACTIVITY_TYPE_PRESETS,
  ALLOWED_ACTIVITY_TYPES,
  DEFAULT_AI_AGENT_CONFIG,
} from '../constants';
import {
  createNewDraft,
  selectWorkflowDraft,
  setSelectedWorkflowId,
  addActivity as addActivityAction,
  setActivities as setActivitiesAction,
  setEditorField as setEditorFieldAction,
  updateActivity as updateActivityAction,
  setDefinitionJson as setDefinitionJsonAction,
} from '../store/workflowEditorSlice';

import type {
  AiAgentNodeConfig,
  WorkflowDefinition,
  WorkflowActivityNode,
  WorkflowActivityCatalogEntry,
} from '../types';

export function useWorkflowEditorState(activityCatalog: WorkflowActivityCatalogEntry[]) {
  const dispatch = useAppDispatch();
  const editor = useAppSelector((state) => state.workflowEditor.draft);
  const activities = useAppSelector((state) => state.workflowEditor.activities);
  const selectedWorkflowId = useAppSelector((state) => state.workflowEditor.selectedWorkflowId);
  const isDirty = useAppSelector((state) => state.workflowEditor.isDirty);

  const [aiDialogOpen, setAiDialogOpen] = useState(false);
  const [aiDialogIndex, setAiDialogIndex] = useState<number | null>(null);
  const [aiTab, setAiTab] = useState(0);

  const hasSelection = useMemo(() => !!editor.id, [editor.id]);

  const allowedTypes = useMemo(
    () => Array.from(new Set([...ALLOWED_ACTIVITY_TYPES, ...activityCatalog.map((x) => x.typeName)])),
    [activityCatalog]
  );

  const requiredConfigByType = useMemo(() => {
    if (activityCatalog.length === 0) {
      return {
        'connect.send_whatsapp_template': ['recipient', 'content'],
        'connect.update_inbox_status': ['messageId', 'status'],
        'connect.enqueue_campaign_message': ['recipient', 'content'],
        'http.request': ['url'],
        'webhook.call': ['url'],
        'storage.write': ['path'],
        'mcp.tool_call': ['server', 'tool'],
        'voice.call': ['phoneNumber', 'script'],
        'callcenter.outbound_call': ['phoneNumber', 'script'],
      } as Record<string, string[]>;
    }

    const map: Record<string, string[]> = {};
    activityCatalog.forEach((entry) => {
      const rules = Object.entries(entry.inputSchema ?? {}).map(([key, descriptor]) =>
        parseSchemaFieldRule(key, descriptor)
      );
      const required = rules.filter((r) => r.required).map((r) => r.key);
      map[entry.typeName] = required;
    });
    return map;
  }, [activityCatalog]);

  const dynamicPresetByType = useMemo(() => {
    const map: Record<string, Record<string, string>> = {};
    activityCatalog.forEach((entry) => {
      const base = { ...(ACTIVITY_TYPE_PRESETS[entry.typeName] ?? {}) };
      const rules = Object.entries(entry.inputSchema ?? {}).map(([key, descriptor]) =>
        parseSchemaFieldRule(key, descriptor)
      );

      rules.forEach((rule) => {
        if (base[rule.key] !== undefined) return;
        if (rule.defaultValue) {
          base[rule.key] = rule.defaultValue;
          return;
        }
        if (rule.key === 'channel') base[rule.key] = 'whatsapp';
        if (rule.key === 'recipient') base[rule.key] = '{{payload.recipient}}';
        if (rule.key === 'content') base[rule.key] = 'Hello from workflow';
      });

      map[entry.typeName] = base;
    });
    return map;
  }, [activityCatalog]);

  const validationErrors = useMemo(
    () => validateWorkflow(activities, editor.triggerEventName, allowedTypes, requiredConfigByType),
    [activities, editor.triggerEventName, allowedTypes, requiredConfigByType]
  );

  const setEditorField = (
    key: 'id' | 'name' | 'triggerEventName' | 'definitionJson',
    value: string
  ) => {
    dispatch(setEditorFieldAction({ field: key, value }));
  };

  const setDefinitionJson = (value: string) => {
    dispatch(setDefinitionJsonAction(value));
  };

  const selectWorkflow = (wf: WorkflowDefinition) => {
    dispatch(selectWorkflowDraft(wf));
  };

  const createNew = () => {
    dispatch(createNewDraft());
  };

  const updateActivity = (index: number, patch: Partial<WorkflowActivityNode>) => {
    dispatch(updateActivityAction({ index, patch }));
  };

  const addActivity = (
    activityType?: string,
    patch: Partial<WorkflowActivityNode> = {}
  ) => {
    const type = activityType ?? allowedTypes[0] ?? 'connect.send_whatsapp_template';
    const preset = dynamicPresetByType[type] ?? ACTIVITY_TYPE_PRESETS[type] ?? {};
    dispatch(
      addActivityAction({
        id: `step-${Date.now()}`,
        type,
        timeoutMs: 30000,
        retryCount: 0,
        retryDelayMs: 0,
        config: { ...preset },
        aiAgent: type === 'ai.agent' ? { ...DEFAULT_AI_AGENT_CONFIG } : undefined,
        ...patch,
      })
    );
  };

  const removeActivity = (index: number) => {
    const next = activities.filter((_, i) => i !== index);
    dispatch(setActivitiesAction(next));
  };

  const applyTypePreset = (index: number, activityType: string) => {
    const preset = dynamicPresetByType[activityType] ?? ACTIVITY_TYPE_PRESETS[activityType] ?? {};
    updateActivity(index, { config: { ...preset } });
  };

  const updateActivityConfig = (index: number, key: string, value: string) => {
    const target = activities[index];
    if (!target) return;
    const nextConfig = { ...(target.config ?? {}), [key]: value };
    const next = activities.map((a, i) => (i === index ? { ...a, config: nextConfig } : a));
    dispatch(setActivitiesAction(next));
  };

  const removeActivityConfig = (index: number, key: string) => {
    const target = activities[index];
    if (!target?.config) return;
    const nextConfig = { ...target.config };
    delete nextConfig[key];
    const next = activities.map((a, i) => (i === index ? { ...a, config: nextConfig } : a));
    dispatch(setActivitiesAction(next));
  };

  const addActivityConfig = (index: number) => {
    const target = activities[index];
    if (!target) return;
    const base = target.config ?? {};
    let key = `key${Object.keys(base).length + 1}`;
    while (Object.prototype.hasOwnProperty.call(base, key)) {
      key = `${key}_x`;
    }
    updateActivityConfig(index, key, '');
  };

  const openAiConfig = (index: number) => {
    setAiDialogIndex(index);
    setAiTab(0);
    setAiDialogOpen(true);
  };

  const closeAiConfig = () => setAiDialogOpen(false);

  const aiTarget =
    aiDialogIndex !== null && aiDialogIndex >= 0 && aiDialogIndex < activities.length
      ? activities[aiDialogIndex]
      : null;

  const updateAiAgentConfig = (patch: Partial<AiAgentNodeConfig>) => {
    if (aiDialogIndex === null) return;
    const target = activities[aiDialogIndex];
    if (!target) return;
    const nextAi: AiAgentNodeConfig = { ...(target.aiAgent ?? DEFAULT_AI_AGENT_CONFIG), ...patch };
    updateActivity(aiDialogIndex, { aiAgent: nextAi });
  };

  const updateAiAgentConfigAt = (index: number, patch: Partial<AiAgentNodeConfig>) => {
    const target = activities[index];
    if (!target) return;
    const nextAi: AiAgentNodeConfig = { ...(target.aiAgent ?? DEFAULT_AI_AGENT_CONFIG), ...patch };
    updateActivity(index, { aiAgent: nextAi });
  };

  return {
    editor,
    activities,
    selectedWorkflowId,
    isDirty,
    hasSelection,
    allowedTypes,
    requiredConfigByType,
    validationErrors,
    aiDialogOpen,
    aiTab,
    aiTarget,
    setAiTab,
    setEditorField,
    setDefinitionJson,
    selectWorkflow,
    createNew,
    addActivity,
    updateActivity,
    removeActivity,
    applyTypePreset,
    addActivityConfig,
    updateActivityConfig,
    removeActivityConfig,
    openAiConfig,
    closeAiConfig,
    updateAiAgentConfig,
    updateAiAgentConfigAt,
    setSelectedWorkflowId: (id: string | null) => dispatch(setSelectedWorkflowId(id)),
  };
}
