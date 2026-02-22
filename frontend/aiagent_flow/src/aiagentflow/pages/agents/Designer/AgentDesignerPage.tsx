import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useEffect } from 'react';
import { useParams } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Tabs from '@mui/material/Tabs';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Paper from '@mui/material/Paper';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import Slider from '@mui/material/Slider';
import Select from '@mui/material/Select';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import InputLabel from '@mui/material/InputLabel';
import IconButton from '@mui/material/IconButton';
import CardContent from '@mui/material/CardContent';
import FormControl from '@mui/material/FormControl';
import { alpha, useTheme } from '@mui/material/styles';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';

import AgentFlowCanvas from './AgentFlowCanvas';
import { saveAgent, publishAgent, fetchAgentDetail } from './designerThunks';
import {
  addStep,
  removeStep,
  resetDraft,
  updateField,
  updateModel,
  setActiveTab,
  updateMemory,
  updateGuardrails,
} from './designerSlice';

import type { AgentStep } from './types';


// ── Helpers ──
const STEP_TYPES = [
  { value: 'think', label: 'Think', icon: 'mdi:head-lightbulb', color: '#7C4DFF' },
  { value: 'plan', label: 'Plan', icon: 'mdi:map-outline', color: '#00BCD4' },
  { value: 'act', label: 'Act', icon: 'mdi:lightning-bolt', color: '#FF9800' },
  { value: 'observe', label: 'Observe', icon: 'mdi:eye-outline', color: '#4CAF50' },
  { value: 'decide', label: 'Decide', icon: 'mdi:source-branch', color: '#E91E63' },
  { value: 'tool_call', label: 'Tool Call', icon: 'mdi:wrench-outline', color: '#607D8B' },
  { value: 'human_review', label: 'Human Review', icon: 'mdi:account-check', color: '#795548' },
] as const;

const MODELS = ['gpt-4o', 'gpt-4o-mini', 'claude-3.5-sonnet', 'gemini-2.0-flash'];

let stepCounter = 0;
const genId = () => `step-${++stepCounter}-${Date.now()}`;

// ══════════════════════════════════════════
// TAB PANELS
// ══════════════════════════════════════════

function TabGeneral({ draft, dispatch }: { draft: any; dispatch: any }) {
  return (
    <Stack spacing={3}>
      <TextField
        fullWidth
        label="Agent Name"
        value={draft.name}
        onChange={(e) => dispatch(updateField({ field: 'name', value: e.target.value }))}
        placeholder="e.g. CustomerSupport-v2"
      />
      <TextField
        fullWidth
        multiline
        rows={3}
        label="Description"
        value={draft.description}
        onChange={(e) => dispatch(updateField({ field: 'description', value: e.target.value }))}
        placeholder="Describe the agent's purpose and capabilities..."
      />
      <Stack direction="row" spacing={2}>
        <TextField
          label="Version"
          value={draft.version}
          onChange={(e) => dispatch(updateField({ field: 'version', value: e.target.value }))}
          sx={{ width: 150 }}
        />
        <FormControl sx={{ minWidth: 150 }}>
          <InputLabel>Status</InputLabel>
          <Select
            value={draft.status}
            label="Status"
            onChange={(e) => dispatch(updateField({ field: 'status', value: e.target.value }))}
          >
            <MenuItem value="Draft">Draft</MenuItem>
            <MenuItem value="Published">Published</MenuItem>
            <MenuItem value="Archived">Archived</MenuItem>
          </Select>
        </FormControl>
      </Stack>
      <TextField
        fullWidth
        multiline
        rows={6}
        label="System Prompt"
        value={draft.systemPrompt}
        onChange={(e) => dispatch(updateField({ field: 'systemPrompt', value: e.target.value }))}
        placeholder="You are a helpful agent that..."
        sx={{ fontFamily: 'monospace' }}
      />
      <Box>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>Tags</Typography>
        <Stack direction="row" spacing={1} flexWrap="wrap">
          {draft.tags.map((tag: string, i: number) => (
            <Chip
              key={i}
              label={tag}
              onDelete={() => {
                const newTags = draft.tags.filter((_: string, idx: number) => idx !== i);
                dispatch(updateField({ field: 'tags', value: newTags }));
              }}
              size="small"
            />
          ))}
          <Chip
            icon={<Iconify icon="mdi:plus" width={16} />}
            label="Add tag"
            variant="outlined"
            size="small"
            onClick={() => {
              const tag = prompt('Enter tag name:');
              if (tag) dispatch(updateField({ field: 'tags', value: [...draft.tags, tag] }));
            }}
          />
        </Stack>
      </Box>
    </Stack>
  );
}

function TabSteps({ draft, dispatch, theme }: { draft: any; dispatch: any; theme: any }) {
  return (
    <Stack spacing={3}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="subtitle1" fontWeight={700}>
          Agent Loop Steps ({draft.steps.length})
        </Typography>
      </Box>

      {/* Add step buttons */}
      <Paper
        variant="outlined"
        sx={{ p: 2, display: 'flex', flexWrap: 'wrap', gap: 1 }}
      >
        {STEP_TYPES.map((st) => (
          <Button
            key={st.value}
            variant="outlined"
            size="small"
            startIcon={<Iconify icon={st.icon} />}
            sx={{
              borderColor: alpha(st.color, 0.5),
              color: st.color,
              '&:hover': { borderColor: st.color, bgcolor: alpha(st.color, 0.08) },
            }}
            onClick={() => {
              const newStep: AgentStep = {
                id: genId(),
                type: st.value,
                label: `${st.label} Step`,
                description: '',
                config: {},
                position: { x: 0, y: draft.steps.length * 80 },
                connections: [],
              };
              dispatch(addStep(newStep));
            }}
          >
            {st.label}
          </Button>
        ))}
      </Paper>

      {/* Step List */}
      {draft.steps.length === 0 && (
        <Alert severity="info" variant="outlined">
          No steps defined yet. Add steps to define the agent&apos;s cognitive loop.
        </Alert>
      )}

      <Stack spacing={1.5}>
        {draft.steps.map((step: AgentStep, idx: number) => {
          const stepType = STEP_TYPES.find(s => s.value === step.type);
          return (
            <Paper
              key={step.id}
              variant="outlined"
              sx={{
                p: 2,
                borderLeft: `4px solid ${stepType?.color || '#999'}`,
                display: 'flex',
                alignItems: 'center',
                gap: 2,
                transition: 'all 0.2s',
                '&:hover': { boxShadow: theme.shadows[4] },
              }}
            >
              <Iconify
                icon={stepType?.icon || 'mdi:help'}
                width={28}
                sx={{ color: stepType?.color }}
              />
              <Box sx={{ flex: 1 }}>
                <Typography variant="subtitle2">{step.label}</Typography>
                <Typography variant="caption" color="text.secondary">
                  Step {idx + 1} · {step.type.replace('_', ' ').toUpperCase()}
                </Typography>
              </Box>
              <Label color="default" variant="soft">{step.type}</Label>
              <Tooltip title="Remove step">
                <IconButton
                  size="small"
                  color="error"
                  onClick={() => dispatch(removeStep(step.id))}
                >
                  <Iconify icon="mdi:delete-outline" width={18} />
                </IconButton>
              </Tooltip>
            </Paper>
          );
        })}
      </Stack>
    </Stack>
  );
}

function TabGuardrails({ draft, dispatch }: { draft: any; dispatch: any }) {
  const theme = useTheme();
  const g = draft.guardrails;
  return (
    <Stack spacing={3}>
      <Typography variant="subtitle1" fontWeight={700}>Execution Limits</Typography>
      <Stack direction="row" spacing={3}>
        <TextField
          label="Max Steps"
          type="number"
          value={g.maxSteps}
          onChange={(e) => dispatch(updateGuardrails({ maxSteps: Number(e.target.value) }))}
          sx={{ width: 160 }}
        />
        <TextField
          label="Timeout/Step (ms)"
          type="number"
          value={g.timeoutPerStepMs}
          onChange={(e) => dispatch(updateGuardrails({ timeoutPerStepMs: Number(e.target.value) }))}
          sx={{ width: 180 }}
        />
        <TextField
          label="Max Tokens"
          type="number"
          value={g.maxTokensPerExecution}
          onChange={(e) => dispatch(updateGuardrails({ maxTokensPerExecution: Number(e.target.value) }))}
          sx={{ width: 180 }}
        />
        <TextField
          label="Max Retries"
          type="number"
          value={g.maxRetries}
          onChange={(e) => dispatch(updateGuardrails({ maxRetries: Number(e.target.value) }))}
          sx={{ width: 140 }}
        />
      </Stack>

      <Divider />
      <Typography variant="subtitle1" fontWeight={700}>Security & Governance</Typography>

      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box>
          <Typography variant="subtitle2">Prompt Injection Guard</Typography>
          <Typography variant="caption" color="text.secondary">Scan all inputs for injection attacks</Typography>
        </Box>
        <Switch
          checked={g.enablePromptInjectionGuard}
          onChange={(e) => dispatch(updateGuardrails({ enablePromptInjectionGuard: e.target.checked }))}
        />
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box>
          <Typography variant="subtitle2">PII Protection</Typography>
          <Typography variant="caption" color="text.secondary">Block responses containing sensitive data</Typography>
        </Box>
        <Switch
          checked={g.enablePIIProtection}
          onChange={(e) => dispatch(updateGuardrails({ enablePIIProtection: e.target.checked }))}
        />
      </Box>

      <Paper variant="outlined" sx={{ p: 2, bgcolor: alpha(theme.palette.info.main, 0.05) }}>
        <Stack spacing={2}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Box>
              <Typography variant="subtitle2" color="info.main">Human-in-the-Loop (HITL)</Typography>
              <Typography variant="caption" color="text.secondary">Enable manual review checkpoints</Typography>
            </Box>
            <Switch
              checked={g.hitl.enabled}
              onChange={(e) => dispatch(updateGuardrails({ hitl: { ...g.hitl, enabled: e.target.checked } }))}
            />
          </Box>

          {g.hitl.enabled && (
             <Stack spacing={2} sx={{ pl: 2, borderLeft: `2px solid ${theme.palette.info.light}` }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <Typography variant="body2">Review all tool calls</Typography>
                  <Switch 
                     size="small"
                     checked={g.hitl.requireReviewOnAllToolCalls}
                     onChange={(e) => dispatch(updateGuardrails({ hitl: { ...g.hitl, requireReviewOnAllToolCalls: e.target.checked } }))}
                  />
                </Box>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <Typography variant="body2">Review on policy escalation</Typography>
                  <Switch 
                     size="small"
                     checked={g.hitl.requireReviewOnPolicyEscalation}
                     onChange={(e) => dispatch(updateGuardrails({ hitl: { ...g.hitl, requireReviewOnPolicyEscalation: e.target.checked } }))}
                  />
                </Box>
                <Box>
                  <Typography variant="body2" sx={{ mb: 1 }}>Confidence threshold to skip review: {g.hitl.confidenceThreshold}</Typography>
                  <Slider
                    value={g.hitl.confidenceThreshold}
                    min={0}
                    max={1}
                    step={0.05}
                    onChange={(_, val) => dispatch(updateGuardrails({ hitl: { ...g.hitl, confidenceThreshold: val as number } }))}
                    valueLabelDisplay="auto"
                  />
                </Box>
             </Stack>
          )}
        </Stack>
      </Paper>
    </Stack>
  );
}

function TabMemory({ draft, dispatch }: { draft: any; dispatch: any }) {
  const m = draft.memory;
  return (
    <Stack spacing={2.5}>
      <Typography variant="subtitle1" fontWeight={700}>Memory Configuration</Typography>
      {([
        { key: 'workingMemory' as const, label: 'Working Memory', desc: 'Short-term context for the current execution', icon: 'mdi:brain' },
        { key: 'longTermMemory' as const, label: 'Long-Term Memory', desc: 'Persistent knowledge across executions (MongoDB)', icon: 'mdi:database' },
        { key: 'vectorMemory' as const, label: 'Vector Memory', desc: 'Semantic search via embeddings (Vector DB)', icon: 'mdi:vector-polyline' },
        { key: 'auditMemory' as const, label: 'Audit Memory', desc: 'Immutable execution log (always enabled)', icon: 'mdi:shield-check' },
      ]).map(({ key, label, desc, icon }) => (
        <Paper key={key} variant="outlined" sx={{ p: 2 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center' }}>
              <Iconify icon={icon} width={24} />
              <Box>
                <Typography variant="subtitle2">{label}</Typography>
                <Typography variant="caption" color="text.secondary">{desc}</Typography>
              </Box>
            </Box>
            <Switch
              checked={m[key]}
              disabled={key === 'auditMemory'}
              onChange={(e) => dispatch(updateMemory({ [key]: e.target.checked }))}
            />
          </Box>
        </Paper>
      ))}
    </Stack>
  );
}

function TabModel({ draft, dispatch }: { draft: any; dispatch: any }) {
  const mc = draft.model;
  return (
    <Stack spacing={3}>
      <Typography variant="subtitle1" fontWeight={700}>LLM Configuration</Typography>
      <Stack direction="row" spacing={3}>
        <FormControl fullWidth>
          <InputLabel>Primary Model</InputLabel>
          <Select
            value={mc.primaryModel}
            label="Primary Model"
            onChange={(e) => dispatch(updateModel({ primaryModel: e.target.value }))}
          >
            {MODELS.map(m => <MenuItem key={m} value={m}>{m}</MenuItem>)}
          </Select>
        </FormControl>
        <FormControl fullWidth>
          <InputLabel>Fallback Model</InputLabel>
          <Select
            value={mc.fallbackModel}
            label="Fallback Model"
            onChange={(e) => dispatch(updateModel({ fallbackModel: e.target.value }))}
          >
            {MODELS.map(m => <MenuItem key={m} value={m}>{m}</MenuItem>)}
          </Select>
        </FormControl>
      </Stack>

      <Box>
        <Typography variant="subtitle2" gutterBottom>
          Temperature: {mc.temperature}
        </Typography>
        <Slider
          value={mc.temperature}
          min={0}
          max={2}
          step={0.1}
          onChange={(_, v) => dispatch(updateModel({ temperature: v as number }))}
          valueLabelDisplay="auto"
        />
        <Stack direction="row" justifyContent="space-between">
          <Typography variant="caption" color="text.secondary">Deterministic</Typography>
          <Typography variant="caption" color="text.secondary">Creative</Typography>
        </Stack>
      </Box>

      <TextField
        label="Max Response Tokens"
        type="number"
        value={mc.maxResponseTokens}
        onChange={(e) => dispatch(updateModel({ maxResponseTokens: Number(e.target.value) }))}
        sx={{ maxWidth: 250 }}
      />
    </Stack>
  );
}

// ══════════════════════════════════════════
// MAIN PAGE
// ══════════════════════════════════════════

const TAB_LABELS = ['General', 'Agent Loop', 'Canvas', 'Guardrails', 'Memory', 'Model'];
const TAB_ICONS = [
  'mdi:information-outline',
  'mdi:repeat',
  'mdi:sitemap',
  'mdi:shield-outline',
  'mdi:brain',
  'mdi:chip',
];

export default function AgentDesignerPage() {
  const dispatch = useDispatch<AppDispatch>();
  const { draft, activeTab, isDirty, saving, errors } = useSelector(
    (state: RootState) => state.designer
  );
  const theme = useTheme();
  const { agentId } = useParams<{ agentId: string }>();

  // Load agent when editing an existing one
  useEffect(() => {
    if (agentId) {
      dispatch(fetchAgentDetail(agentId));
    } else {
      dispatch(resetDraft());
    }
  }, [agentId, dispatch]);

  const handleSave = () => {
    dispatch(saveAgent(draft));
  };

  const handlePublish = () => {
    if (draft.id) {
      dispatch(publishAgent(draft.id));
    }
  };

  const renderTabContent = () => {
    switch (activeTab) {
      case 0:
        return <TabGeneral draft={draft} dispatch={dispatch} />;
      case 1:
        return <TabSteps draft={draft} dispatch={dispatch} theme={theme} />;
      case 2:
        return <AgentFlowCanvas steps={draft.steps} />;
      case 3:
        return <TabGuardrails draft={draft} dispatch={dispatch} />;
      case 4:
        return <TabMemory draft={draft} dispatch={dispatch} />;
      case 5:
        return <TabModel draft={draft} dispatch={dispatch} />;
      default:
        return null;
    }
  };

  return (
    <>
      <Helmet>
        <title>{draft.name || 'New Agent'} — Designer | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        {/* Error Banner */}
        {Object.keys(errors).length > 0 && (
          <Alert severity="error" sx={{ mb: 3 }}>
            {Object.values(errors).join(' · ')}
          </Alert>
        )}

        {/* Header */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 4 }}>
          <Box>
            <Typography variant="h4">Agent Designer</Typography>
            <Typography variant="body2" color="text.secondary">
              {draft.name || 'Untitled Agent'} · v{draft.version}
              {isDirty && (
                <Chip
                  label="Unsaved changes"
                  size="small"
                  color="warning"
                  variant="soft"
                  sx={{ ml: 1 }}
                />
              )}
              {saving && (
                <Chip label="Saving..." size="small" color="info" variant="soft" sx={{ ml: 1 }} />
              )}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1.5}>
            <Button
              variant="outlined"
              color="inherit"
              startIcon={<Iconify icon="mdi:refresh" />}
              onClick={() => dispatch(resetDraft())}
            >
              Reset
            </Button>
            <Button
              variant="contained"
              startIcon={<Iconify icon="mdi:content-save" />}
              disabled={!isDirty || saving}
              onClick={handleSave}
            >
              {saving ? 'Saving...' : 'Save Draft'}
            </Button>
            <Button
              variant="contained"
              color="success"
              startIcon={<Iconify icon="mdi:rocket-launch" />}
              disabled={!draft.name || draft.steps.length === 0 || !draft.id || saving}
              onClick={handlePublish}
            >
              Publish
            </Button>
          </Stack>
        </Box>

        {/* Tabs */}
        <Card
          sx={{
            border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
          }}
        >
          <Tabs
            value={activeTab}
            onChange={(_, v) => dispatch(setActiveTab(v))}
            sx={{
              px: 3,
              borderBottom: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
            }}
          >
            {TAB_LABELS.map((label, i) => (
              <Tab
                key={label}
                label={label}
                icon={<Iconify icon={TAB_ICONS[i]} width={20} />}
                iconPosition="start"
              />
            ))}
          </Tabs>
          <CardContent sx={{ p: 4 }}>{renderTabContent()}</CardContent>
        </Card>
      </DashboardContent>
    </>
  );
}

