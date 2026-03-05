import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

type Rule = {
  ruleName: string;
  matchSegments: string[];
  targetAgentId: string;
  priority: number;
  requireAllSegments: boolean;
};

export default function SegmentRoutingPage() {
  const tenantId = useTenantId();
  const [agents, setAgents] = useState<any[]>([]);
  const [agentId, setAgentId] = useState('');
  const [defaultTargetAgentId, setDefaultTargetAgentId] = useState('');
  const [isEnabled, setIsEnabled] = useState(true);
  const [rules, setRules] = useState<Rule[]>([]);
  const [previewSegments, setPreviewSegments] = useState('beta');
  const [previewResult, setPreviewResult] = useState<any>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const loadAgents = async () => {
      const res = await axios.get(endpoints.agentflow.agents.list(tenantId));
      const list = (res.data ?? []).filter((a: any) => a?.id);
      setAgents(list);
      if (!agentId && list.length > 0) {
        setAgentId(list[0].id);
      }
    };
    void loadAgents();
  }, [tenantId]);

  const loadConfig = async () => {
    if (!agentId) return;
    try {
      const res = await axios.get(endpoints.agentflow.segmentRouting.getConfig(tenantId, agentId));
      setIsEnabled(!!res.data?.isEnabled);
      setRules(res.data?.rules ?? []);
      setDefaultTargetAgentId(res.data?.defaultTargetAgentId ?? '');
      setMessage('Configuration loaded');
    } catch {
      setRules([]);
      setDefaultTargetAgentId('');
      setMessage('No existing config for this agent');
    }
  };

  const addRule = () => {
    setRules((prev) => [
      ...prev,
      { ruleName: `rule-${prev.length + 1}`, matchSegments: ['beta'], targetAgentId: '', priority: prev.length + 1, requireAllSegments: false },
    ]);
  };

  const saveConfig = async () => {
    if (!agentId) return;
    await axios.put(endpoints.agentflow.segmentRouting.updateConfig(tenantId, agentId), {
      isEnabled,
      rules,
      defaultTargetAgentId: defaultTargetAgentId || null,
    });
    setMessage('Segment routing saved');
  };

  const runPreview = async () => {
    if (!agentId) return;
    const res = await axios.post(endpoints.agentflow.segmentRouting.preview(tenantId, agentId), {
      userId: 'preview-user',
      userSegments: previewSegments.split(',').map((s) => s.trim()).filter(Boolean),
      metadata: {},
    });
    setPreviewResult(res.data);
  };

  return (
    <>
      <Helmet><title>Segment Routing | {CONFIG.appName}</title></Helmet>
      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Typography variant="h4">Segment Routing</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Route users to different agents by segment rules.
          </Typography>
        </Box>

        {message && <Alert severity="info" sx={{ mb: 2 }}>{message}</Alert>}

        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Grid container spacing={2}>
              <Grid item xs={12} md={4}>
                <TextField select fullWidth label="Source Agent" value={agentId} onChange={(e) => setAgentId(e.target.value)}>
                  {agents.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                </TextField>
              </Grid>
              <Grid item xs={12} md={4}>
                <TextField select fullWidth label="Default Target Agent" value={defaultTargetAgentId} onChange={(e) => setDefaultTargetAgentId(e.target.value)}>
                  <MenuItem value="">(none)</MenuItem>
                  {agents.filter((a) => a.id !== agentId).map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                </TextField>
              </Grid>
              <Grid item xs={12} md={4}>
                <TextField select fullWidth label="Enabled" value={isEnabled ? 'yes' : 'no'} onChange={(e) => setIsEnabled(e.target.value === 'yes')}>
                  <MenuItem value="yes">Yes</MenuItem>
                  <MenuItem value="no">No</MenuItem>
                </TextField>
              </Grid>
            </Grid>
            <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
              <Button variant="outlined" onClick={loadConfig}>Load</Button>
              <Button variant="outlined" onClick={addRule}>Add Rule</Button>
              <Button variant="contained" onClick={saveConfig}>Save</Button>
            </Stack>
          </CardContent>
        </Card>

        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h6" sx={{ mb: 2 }}>Rules</Typography>
            <Stack spacing={2}>
              {rules.map((r, idx) => (
                <Grid key={`${r.ruleName}-${idx}`} container spacing={2}>
                  <Grid item xs={12} md={3}><TextField fullWidth label="Rule Name" value={r.ruleName} onChange={(e) => setRules((prev) => prev.map((x, i) => i === idx ? { ...x, ruleName: e.target.value } : x))} /></Grid>
                  <Grid item xs={12} md={3}><TextField fullWidth label="Segments" value={r.matchSegments.join(',')} onChange={(e) => setRules((prev) => prev.map((x, i) => i === idx ? { ...x, matchSegments: e.target.value.split(',').map((s) => s.trim()).filter(Boolean) } : x))} /></Grid>
                  <Grid item xs={12} md={3}>
                    <TextField select fullWidth label="Target Agent" value={r.targetAgentId} onChange={(e) => setRules((prev) => prev.map((x, i) => i === idx ? { ...x, targetAgentId: e.target.value } : x))}>
                      {agents.filter((a) => a.id !== agentId).map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                    </TextField>
                  </Grid>
                  <Grid item xs={12} md={3}><TextField fullWidth type="number" label="Priority" value={r.priority} onChange={(e) => setRules((prev) => prev.map((x, i) => i === idx ? { ...x, priority: Number(e.target.value || 0) } : x))} /></Grid>
                </Grid>
              ))}
            </Stack>
          </CardContent>
        </Card>

        <Card>
          <CardContent>
            <Typography variant="h6" sx={{ mb: 2 }}>Preview</Typography>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
              <TextField fullWidth label="User Segments" value={previewSegments} onChange={(e) => setPreviewSegments(e.target.value)} />
              <Button variant="contained" onClick={runPreview}>Run Preview</Button>
            </Stack>
            {previewResult && (
              <Alert severity={previewResult.wasRouted ? 'success' : 'info'} sx={{ mt: 2 }}>
                Selected: <strong>{previewResult.selectedAgentId}</strong> · Reason: {previewResult.reason}
              </Alert>
            )}
          </CardContent>
        </Card>
      </DashboardContent>
    </>
  );
}
