import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TextField from '@mui/material/TextField';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import CircularProgress from '@mui/material/CircularProgress';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

type WorkflowDefinition = {
  id: string;
  name: string;
  triggerEventName: string;
  version: number;
  status: 'Draft' | 'Published' | 'Archived' | string;
  definitionJson: string;
  updatedAt: string;
  updatedBy: string;
};

type WorkflowExecution = {
  id: string;
  workflowDefinitionId: string;
  triggerEventName: string;
  correlationId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | string;
  error?: string | null;
  createdAt: string;
  updatedAt: string;
  requestedBy: string;
};

type WorkflowStep = {
  id: string;
  activityType: string;
  activityName: string;
  status: string;
  error?: string | null;
  startedAt: string;
  completedAt?: string | null;
};

type WorkflowAuditEvent = {
  id: string;
  executionId: string;
  workflowId: string;
  actor: string;
  correlationId: string;
  occurredAt: string;
  eventJson: string;
};

type WorkflowActivityNode = {
  id: string;
  type: string;
  name?: string;
  next?: string;
  onSuccess?: string;
  onFailure?: string;
  timeoutMs?: number;
  retryCount?: number;
  retryDelayMs?: number;
  config?: Record<string, string>;
};

const DEFAULT_DEFINITION = JSON.stringify(
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

export default function WorkflowsPage() {
  const tenantId = useTenantId();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [workflows, setWorkflows] = useState<WorkflowDefinition[]>([]);
  const [executions, setExecutions] = useState<WorkflowExecution[]>([]);
  const [selected, setSelected] = useState<WorkflowDefinition | null>(null);
  const [steps, setSteps] = useState<WorkflowStep[]>([]);
  const [auditEvents, setAuditEvents] = useState<WorkflowAuditEvent[]>([]);
  const [activities, setActivities] = useState<WorkflowActivityNode[]>([]);
  const [stepsOpen, setStepsOpen] = useState(false);
  const [metrics, setMetrics] = useState<any>(null);

  const [editor, setEditor] = useState({
    id: '',
    name: '',
    triggerEventName: 'connect.message.received',
    definitionJson: DEFAULT_DEFINITION,
  });

  const hasSelection = useMemo(() => !!editor.id, [editor.id]);

  const syncActivitiesFromJson = (definitionJson: string) => {
    try {
      const parsed = JSON.parse(definitionJson) as { activities?: WorkflowActivityNode[] };
      setActivities(parsed.activities ?? []);
    } catch {
      setActivities([]);
    }
  };

  const syncJsonFromActivities = (nextActivities: WorkflowActivityNode[]) => {
    try {
      const parsed = JSON.parse(editor.definitionJson) as Record<string, any>;
      parsed.activities = nextActivities;
      setEditor((prev) => ({ ...prev, definitionJson: JSON.stringify(parsed, null, 2) }));
    } catch {
      setEditor((prev) => ({
        ...prev,
        definitionJson: JSON.stringify({ activities: nextActivities }, null, 2),
      }));
    }
  };

  const loadAll = async () => {
    try {
      setLoading(true);
      setError(null);
      const [wfRes, exRes, metRes, auditRes] = await Promise.all([
        axios.get(endpoints.agentflow.workflows.list(tenantId)),
        axios.get(endpoints.agentflow.workflows.executions(tenantId)),
        axios.get(endpoints.agentflow.workflows.metrics(tenantId)),
        axios.get(endpoints.agentflow.workflows.auditEvents(tenantId)),
      ]);
      setWorkflows((wfRes.data ?? []) as WorkflowDefinition[]);
      setExecutions((exRes.data ?? []) as WorkflowExecution[]);
      setMetrics(metRes.data ?? null);
      setAuditEvents((auditRes.data ?? []) as WorkflowAuditEvent[]);
    } catch (e: any) {
      setError(e?.message || 'Failed to load workflows');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const selectWorkflow = (wf: WorkflowDefinition) => {
    setSelected(wf);
    setEditor({
      id: wf.id,
      name: wf.name,
      triggerEventName: wf.triggerEventName,
      definitionJson: wf.definitionJson,
    });
    syncActivitiesFromJson(wf.definitionJson);
  };

  const createNew = () => {
    setSelected(null);
    setEditor({
      id: `wf_${Date.now()}`,
      name: 'New Workflow',
      triggerEventName: 'connect.message.received',
      definitionJson: DEFAULT_DEFINITION,
    });
    syncActivitiesFromJson(DEFAULT_DEFINITION);
  };

  const saveWorkflow = async () => {
    if (!editor.id || !editor.name.trim()) return;
    try {
      setSaving(true);
      JSON.parse(editor.definitionJson);
      await axios.put(endpoints.agentflow.workflows.upsert(tenantId, editor.id), {
        name: editor.name.trim(),
        triggerEventName: editor.triggerEventName.trim(),
        definitionJson: editor.definitionJson,
        metadata: {},
      });
      await loadAll();
    } catch (e: any) {
      setError(e?.message || 'Failed to save workflow');
    } finally {
      setSaving(false);
    }
  };

  const addActivity = () => {
    const next: WorkflowActivityNode[] = [
      ...activities,
      {
        id: `step_${activities.length + 1}`,
        type: 'connect.send_whatsapp_template',
        timeoutMs: 30000,
        retryCount: 0,
        retryDelayMs: 0,
      },
    ];
    setActivities(next);
    syncJsonFromActivities(next);
  };

  const updateActivity = (index: number, patch: Partial<WorkflowActivityNode>) => {
    const next = activities.map((a, i) => (i === index ? { ...a, ...patch } : a));
    setActivities(next);
    syncJsonFromActivities(next);
  };

  const updateActivityConfig = (index: number, key: string, value: string) => {
    const target = activities[index];
    if (!target) return;
    const nextConfig = { ...(target.config ?? {}), [key]: value };
    const next = activities.map((a, i) => (i === index ? { ...a, config: nextConfig } : a));
    setActivities(next);
    syncJsonFromActivities(next);
  };

  const removeActivityConfig = (index: number, key: string) => {
    const target = activities[index];
    if (!target?.config) return;
    const nextConfig = { ...target.config };
    delete nextConfig[key];
    const next = activities.map((a, i) => (i === index ? { ...a, config: nextConfig } : a));
    setActivities(next);
    syncJsonFromActivities(next);
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

  const removeActivity = (index: number) => {
    const next = activities.filter((_, i) => i !== index);
    setActivities(next);
    syncJsonFromActivities(next);
  };

  const publishWorkflow = async () => {
    if (!hasSelection) return;
    try {
      await axios.post(endpoints.agentflow.workflows.publish(tenantId, editor.id));
      await loadAll();
    } catch (e: any) {
      setError(e?.message || 'Failed to publish workflow');
    }
  };

  const runEvent = async () => {
    try {
      setRunning(true);
      await axios.post(endpoints.agentflow.workflows.runEvent(tenantId), {
        eventName: editor.triggerEventName || 'connect.message.received',
        payload: {
          channel: 'whatsapp',
          recipient: '5215555555555',
          customerName: 'Demo User',
          content: 'Hola desde Studio Workflows',
        },
      });
      await loadAll();
    } catch (e: any) {
      setError(e?.message || 'Failed to run workflow event');
    } finally {
      setRunning(false);
    }
  };

  const retryExecution = async (executionId: string) => {
    try {
      await axios.post(endpoints.agentflow.workflows.retry(tenantId, executionId));
      await loadAll();
    } catch (e: any) {
      setError(e?.message || 'Failed to retry execution');
    }
  };

  const openSteps = async (executionId: string) => {
    try {
      const res = await axios.get(endpoints.agentflow.workflows.steps(tenantId, executionId));
      setSteps((res.data ?? []) as WorkflowStep[]);
      setStepsOpen(true);
    } catch (e: any) {
      setError(e?.message || 'Failed to load execution steps');
    }
  };

  return (
    <>
      <Helmet>
        <title>Studio Workflows | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 3, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Studio Workflows</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
              Build, publish and run workflow automations connected to Connect channels.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" />} onClick={loadAll}>
              Refresh
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={createNew}>
              New
            </Button>
          </Stack>
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        <Grid container spacing={3}>
          <Grid item xs={12} md={4}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>
                Definitions
              </Typography>
              {loading ? (
                <Box sx={{ py: 4, textAlign: 'center' }}>
                  <CircularProgress />
                </Box>
              ) : workflows.length === 0 ? (
                <Alert severity="info">No workflows found. Create your first definition.</Alert>
              ) : (
                <Stack spacing={1}>
                  {workflows.map((wf) => (
                    <Box
                      key={wf.id}
                      sx={{
                        p: 1.5,
                        border: 1,
                        borderColor: selected?.id === wf.id ? 'primary.main' : 'divider',
                        borderRadius: 1,
                        bgcolor: selected?.id === wf.id ? 'action.selected' : 'background.paper',
                        cursor: 'pointer',
                      }}
                      onClick={() => selectWorkflow(wf)}
                    >
                      <Typography variant="subtitle2">{wf.name}</Typography>
                      <Typography variant="caption" color="text.secondary" display="block">
                        {wf.triggerEventName}
                      </Typography>
                      <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                        <Chip size="small" label={wf.status} color={wf.status === 'Published' ? 'success' : 'default'} />
                        <Chip size="small" variant="outlined" label={`v${wf.version}`} />
                      </Stack>
                    </Box>
                  ))}
                </Stack>
              )}
            </Card>
          </Grid>

          <Grid item xs={12} md={8}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>
                Editor
              </Typography>
              <Stack spacing={2}>
                <TextField
                  label="Workflow ID"
                  value={editor.id}
                  onChange={(e) => setEditor((prev) => ({ ...prev, id: e.target.value }))}
                  fullWidth
                />
                <TextField
                  label="Name"
                  value={editor.name}
                  onChange={(e) => setEditor((prev) => ({ ...prev, name: e.target.value }))}
                  fullWidth
                />
                <TextField
                  label="Trigger Event"
                  value={editor.triggerEventName}
                  onChange={(e) => setEditor((prev) => ({ ...prev, triggerEventName: e.target.value }))}
                  fullWidth
                />
                <TextField
                  label="Definition JSON"
                  value={editor.definitionJson}
                  onChange={(e) => {
                    const value = e.target.value;
                    setEditor((prev) => ({ ...prev, definitionJson: value }));
                    syncActivitiesFromJson(value);
                  }}
                  multiline
                  minRows={14}
                  maxRows={24}
                  fullWidth
                />
                <Card variant="outlined" sx={{ p: 2 }}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
                    <Typography variant="subtitle1">Visual Designer (MVP)</Typography>
                    <Button size="small" onClick={addActivity} startIcon={<Iconify icon="mingcute:add-line" />}>
                      Add Step
                    </Button>
                  </Stack>
                  <Stack spacing={1}>
                    {activities.map((activity, idx) => (
                      <Box key={`${activity.id}_${idx}`} sx={{ p: 1.2, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                        <Grid container spacing={1}>
                          <Grid item xs={12} md={3}>
                            <TextField
                              label="ID"
                              size="small"
                              value={activity.id}
                              onChange={(e) => updateActivity(idx, { id: e.target.value })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              label="Type"
                              size="small"
                              value={activity.type}
                              onChange={(e) => updateActivity(idx, { type: e.target.value })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={3}>
                            <TextField
                              label="Name"
                              size="small"
                              value={activity.name ?? ''}
                              onChange={(e) => updateActivity(idx, { name: e.target.value || undefined })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={3}>
                            <TextField
                              label="On Success"
                              size="small"
                              value={activity.onSuccess ?? ''}
                              onChange={(e) => updateActivity(idx, { onSuccess: e.target.value || undefined })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={3}>
                            <TextField
                              label="On Failure"
                              size="small"
                              value={activity.onFailure ?? ''}
                              onChange={(e) => updateActivity(idx, { onFailure: e.target.value || undefined })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={3}>
                            <TextField
                              label="Next"
                              size="small"
                              value={activity.next ?? ''}
                              onChange={(e) => updateActivity(idx, { next: e.target.value || undefined })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={2}>
                            <TextField
                              label="Timeout"
                              size="small"
                              type="number"
                              value={activity.timeoutMs ?? 30000}
                              onChange={(e) => updateActivity(idx, { timeoutMs: Number(e.target.value || 30000) })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={2}>
                            <TextField
                              label="Retry"
                              size="small"
                              type="number"
                              value={activity.retryCount ?? 0}
                              onChange={(e) => updateActivity(idx, { retryCount: Number(e.target.value || 0) })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={2}>
                            <TextField
                              label="Delay"
                              size="small"
                              type="number"
                              value={activity.retryDelayMs ?? 0}
                              onChange={(e) => updateActivity(idx, { retryDelayMs: Number(e.target.value || 0) })}
                              fullWidth
                            />
                          </Grid>
                          <Grid item xs={12} md={2}>
                            <Button color="error" variant="outlined" onClick={() => removeActivity(idx)} fullWidth>
                              Remove
                            </Button>
                          </Grid>
                          <Grid item xs={12}>
                            <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 0.8 }}>
                              <Typography variant="caption" color="text.secondary">Config</Typography>
                              <Button size="small" onClick={() => addActivityConfig(idx)}>Add Config</Button>
                            </Stack>
                            <Stack spacing={0.8}>
                              {Object.entries(activity.config ?? {}).map(([key, value]) => (
                                <Grid container spacing={1} key={`${activity.id}_${key}`}>
                                  <Grid item xs={12} md={4}>
                                    <TextField
                                      label="Key"
                                      size="small"
                                      value={key}
                                      onChange={(e) => {
                                        const nextKey = e.target.value.trim();
                                        if (!nextKey || nextKey === key) return;
                                        const cfg = { ...(activity.config ?? {}) };
                                        const currentValue = cfg[key] ?? '';
                                        delete cfg[key];
                                        cfg[nextKey] = currentValue;
                                        updateActivity(idx, { config: cfg });
                                      }}
                                      fullWidth
                                    />
                                  </Grid>
                                  <Grid item xs={12} md={7}>
                                    <TextField
                                      label="Value"
                                      size="small"
                                      value={value}
                                      onChange={(e) => updateActivityConfig(idx, key, e.target.value)}
                                      fullWidth
                                    />
                                  </Grid>
                                  <Grid item xs={12} md={1}>
                                    <Button color="error" onClick={() => removeActivityConfig(idx, key)} fullWidth>
                                      X
                                    </Button>
                                  </Grid>
                                </Grid>
                              ))}
                              {Object.keys(activity.config ?? {}).length === 0 && (
                                <Typography variant="caption" color="text.secondary">
                                  No config values yet.
                                </Typography>
                              )}
                            </Stack>
                          </Grid>
                        </Grid>
                      </Box>
                    ))}
                    {activities.length === 0 && <Alert severity="info">No steps found in JSON definition.</Alert>}
                  </Stack>
                </Card>
                <Stack direction="row" spacing={1}>
                  <Button variant="contained" onClick={saveWorkflow} disabled={saving || !editor.id}>
                    {saving ? 'Saving...' : 'Save'}
                  </Button>
                  <Button variant="outlined" onClick={publishWorkflow} disabled={!hasSelection}>
                    Publish
                  </Button>
                  <Button variant="outlined" color="success" onClick={runEvent} disabled={running}>
                    {running ? 'Running...' : 'Run Event'}
                  </Button>
                </Stack>
              </Stack>
            </Card>
          </Grid>
        </Grid>

        <Grid container spacing={3} sx={{ mt: 1 }}>
          <Grid item xs={12} md={8}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>
                Recent Executions
              </Typography>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>ID</TableCell>
                    <TableCell>Workflow</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell>Created</TableCell>
                    <TableCell align="right">Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {executions.slice(0, 12).map((ex) => (
                    <TableRow key={ex.id}>
                      <TableCell>{ex.id.slice(0, 8)}...</TableCell>
                      <TableCell>{ex.workflowDefinitionId.slice(0, 8)}...</TableCell>
                      <TableCell>
                        <Chip
                          size="small"
                          label={ex.status}
                          color={ex.status === 'Completed' ? 'success' : ex.status === 'Failed' ? 'error' : 'default'}
                        />
                      </TableCell>
                      <TableCell>{new Date(ex.createdAt).toLocaleString()}</TableCell>
                      <TableCell align="right">
                        <Stack direction="row" spacing={1} justifyContent="flex-end">
                          <IconButton size="small" onClick={() => openSteps(ex.id)}>
                            <Iconify icon="mdi:format-list-bulleted" />
                          </IconButton>
                          {ex.status === 'Failed' && (
                            <Button size="small" color="warning" onClick={() => retryExecution(ex.id)}>
                              Retry
                            </Button>
                          )}
                        </Stack>
                      </TableCell>
                    </TableRow>
                  ))}
                  {executions.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={5}>
                        <Alert severity="info">No workflow executions yet.</Alert>
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </Card>
          </Grid>

          <Grid item xs={12} md={4}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>
                Runtime Metrics
              </Typography>
              {!metrics ? (
                <Alert severity="info">Metrics unavailable.</Alert>
              ) : (
                <Stack spacing={1.2}>
                  <Typography variant="body2">Total: <strong>{metrics.total ?? 0}</strong></Typography>
                  <Typography variant="body2">Success Rate: <strong>{Math.round((metrics.successRate ?? 0) * 100)}%</strong></Typography>
                  <Typography variant="body2">Failure Rate: <strong>{Math.round((metrics.failureRate ?? 0) * 100)}%</strong></Typography>
                  <Typography variant="body2">Avg Latency: <strong>{metrics.avgLatencyMs ?? 0} ms</strong></Typography>
                  <Typography variant="subtitle2" sx={{ mt: 1 }}>Top Activities</Typography>
                  <Stack spacing={0.8}>
                    {(metrics.activityMetrics ?? []).slice(0, 5).map((a: any) => (
                      <Box key={a.activityType} sx={{ p: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                        <Typography variant="caption" fontWeight={700}>{a.activityType}</Typography>
                        <Typography variant="caption" display="block" color="text.secondary">
                          {a.succeeded}/{a.total} success - {a.avgLatencyMs} ms avg
                        </Typography>
                      </Box>
                    ))}
                  </Stack>
                  <Typography variant="subtitle2" sx={{ mt: 1 }}>Recent Audit</Typography>
                  <Stack spacing={0.8}>
                    {auditEvents.slice(0, 5).map((event) => (
                      <Box key={event.id} sx={{ p: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                        <Typography variant="caption" fontWeight={700}>{event.actor}</Typography>
                        <Typography variant="caption" display="block" color="text.secondary">
                          {new Date(event.occurredAt).toLocaleString()}
                        </Typography>
                      </Box>
                    ))}
                    {auditEvents.length === 0 && <Typography variant="caption" color="text.secondary">No audit events yet.</Typography>}
                  </Stack>
                </Stack>
              )}
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      <Dialog open={stepsOpen} onClose={() => setStepsOpen(false)} fullWidth maxWidth="md">
        <DialogTitle>Execution Steps</DialogTitle>
        <DialogContent>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Activity</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Started</TableCell>
                <TableCell>Completed</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {steps.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>
                    <Typography variant="body2">{s.activityName}</Typography>
                    <Typography variant="caption" color="text.secondary">{s.activityType}</Typography>
                  </TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      label={s.status}
                      color={s.status === 'Completed' ? 'success' : s.status === 'Failed' ? 'error' : 'default'}
                    />
                  </TableCell>
                  <TableCell>{new Date(s.startedAt).toLocaleString()}</TableCell>
                  <TableCell>{s.completedAt ? new Date(s.completedAt).toLocaleString() : '-'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setStepsOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
