import type { GridColDef } from '@mui/x-data-grid';

import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import { DataGrid } from '@mui/x-data-grid';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

type Agent = { id: string; name: string };
type SessionRow = {
  id: string;
  channelId?: string;
  channelType?: string;
  identifier?: string;
  agentId?: string;
  threadId?: string;
  status?: string;
  messageCount?: number;
  lastActivityAt?: string;
};

type ExecutionRow = {
  id: string;
  status: string;
  createdAt: string;
  durationMs?: number;
  totalSteps?: number;
  totalTokensUsed?: number;
  correlationId?: string;
};

export default function ManagerOrchestrationPage() {
  const tenantId = useTenantId();

  const [agents, setAgents] = useState<Agent[]>([]);
  const [sourceAgentId, setSourceAgentId] = useState('');
  const [targetAgentId, setTargetAgentId] = useState('');
  const [intent, setIntent] = useState('general_support');
  const [sessionId, setSessionId] = useState(`sess-${Date.now()}`);
  const [payloadJson, setPayloadJson] = useState('{"input":"hello"}');

  const [allowedTargets, setAllowedTargets] = useState<string[]>([]);
  const [policyDecision, setPolicyDecision] = useState<any>(null);
  const [handoffResult, setHandoffResult] = useState<any>(null);
  const [sessions, setSessions] = useState<SessionRow[]>([]);
  const [selectedSession, setSelectedSession] = useState<SessionRow | null>(null);
  const [sessionExecutions, setSessionExecutions] = useState<ExecutionRow[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const load = async () => {
      try {
        setLoading(true);
        const [agentsRes, sessionsRes] = await Promise.all([
          axios.get(endpoints.agentflow.agents.list(tenantId)),
          axios.get(`/api/v1/tenants/${tenantId}/channel-sessions?limit=50`),
        ]);

        const agentList: Agent[] = (agentsRes.data ?? [])
          .filter((a: any) => a?.id)
          .map((a: any) => ({ id: a.id, name: a.name }));

        setAgents(agentList);
        if (!sourceAgentId && agentList.length > 0) {
          setSourceAgentId(agentList[0].id);
        }

        setSessions((sessionsRes.data ?? []) as SessionRow[]);
      } catch (e: any) {
        setMessage(e?.message ?? 'Failed to load orchestration data');
      } finally {
        setLoading(false);
      }
    };

    void load();
  }, [tenantId]);

  const availableTargets = useMemo(
    () => agents.filter((a) => a.id !== sourceAgentId),
    [agents, sourceAgentId]
  );

  const loadAllowedTargets = async () => {
    if (!sourceAgentId) return;
    try {
      setMessage(null);
      const res = await axios.get(endpoints.agentflow.executions.handoffAllowedTargets(tenantId, sourceAgentId));
      const targets = (res.data?.allowedTargets ?? res.data?.targets ?? []) as string[];
      setAllowedTargets(targets);
      setMessage(`Loaded ${targets.length} allowed sub-agents for manager ${sourceAgentId}`);
    } catch (e: any) {
      setMessage(e?.message ?? 'Failed to load allowed targets');
      setAllowedTargets([]);
    }
  };

  const evaluateDecision = async () => {
    if (!sourceAgentId || !targetAgentId) return;
    try {
      setMessage(null);
      const res = await axios.get(
        endpoints.agentflow.executions.handoffDecision(tenantId, sourceAgentId, targetAgentId)
      );
      setPolicyDecision(res.data);
    } catch (e: any) {
      setMessage(e?.message ?? 'Failed to evaluate policy decision');
      setPolicyDecision(null);
    }
  };

  const executeHandoff = async () => {
    if (!sourceAgentId || !targetAgentId || !sessionId || !intent) return;
    try {
      setMessage(null);
      const payload = (() => {
        try {
          return JSON.parse(payloadJson || '{}');
        } catch {
          return { raw: payloadJson };
        }
      })();

      const res = await axios.post(`/api/v1/tenants/${tenantId}/agents/${sourceAgentId}/handoff`, {
        sessionId,
        targetAgentId,
        intent,
        payloadJson: JSON.stringify(payload),
        metadata: {
          initiatedFrom: 'manager-orchestration-ui',
        },
      });

      setHandoffResult(res.data);
      setMessage('Handoff executed');
    } catch (e: any) {
      setMessage(e?.message ?? 'Handoff failed');
      setHandoffResult(null);
    }
  };

  const inspectSession = async (row: SessionRow) => {
    setSelectedSession(row);
    setSessionExecutions([]);
    if (!row.agentId || !row.id) return;

    try {
      const res = await axios.get(
        `${endpoints.agentflow.executions.byAgent(tenantId, row.agentId)}?sessionId=${encodeURIComponent(row.id)}&limit=50`
      );
      setSessionExecutions((res.data ?? []) as ExecutionRow[]);
    } catch {
      setSessionExecutions([]);
    }
  };

  const sessionColumns: GridColDef[] = [
    { field: 'id', headerName: 'Session', flex: 1, minWidth: 200 },
    { field: 'channelType', headerName: 'Channel', width: 120 },
    { field: 'identifier', headerName: 'Identifier', flex: 1, minWidth: 140 },
    { field: 'agentId', headerName: 'Owner Agent', width: 180 },
    { field: 'messageCount', headerName: 'Msgs', width: 90, type: 'number' },
    {
      field: 'status',
      headerName: 'Status',
      width: 120,
      renderCell: (p) => <Chip size="small" label={p.value ?? 'unknown'} color={p.value === 'Active' ? 'success' : 'default'} />,
    },
  ];

  const execColumns: GridColDef[] = [
    { field: 'id', headerName: 'Execution', flex: 1, minWidth: 180 },
    { field: 'status', headerName: 'Status', width: 120 },
    { field: 'createdAt', headerName: 'Created', width: 170, valueFormatter: (v) => (v ? new Date(v as string).toLocaleString() : '') },
    { field: 'totalSteps', headerName: 'Steps', width: 90, type: 'number' },
    { field: 'totalTokensUsed', headerName: 'Tokens', width: 100, type: 'number' },
    { field: 'correlationId', headerName: 'Correlation', flex: 1, minWidth: 180 },
  ];

  return (
    <>
      <Helmet>
        <title>Manager Orchestration | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Typography variant="h4">Manager Orchestration</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Intent routing control center: policy decision, handoff simulation and live session ownership.
          </Typography>
        </Box>

        {message && <Alert severity="info" sx={{ mb: 2 }}>{message}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} lg={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>Handoff Simulator</Typography>
                <Stack spacing={2}>
                  <TextField select label="Manager Agent" value={sourceAgentId} onChange={(e) => setSourceAgentId(e.target.value)}>
                    {agents.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                  </TextField>

                  <TextField select label="Target Sub-agent" value={targetAgentId} onChange={(e) => setTargetAgentId(e.target.value)}>
                    {availableTargets.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                  </TextField>

                  <TextField label="Intent" value={intent} onChange={(e) => setIntent(e.target.value)} />
                  <TextField label="Session ID" value={sessionId} onChange={(e) => setSessionId(e.target.value)} />
                  <TextField label="Payload JSON" value={payloadJson} onChange={(e) => setPayloadJson(e.target.value)} multiline minRows={3} />

                  <Stack direction="row" spacing={1}>
                    <Button variant="outlined" onClick={loadAllowedTargets} disabled={loading}>Allowed Targets</Button>
                    <Button variant="outlined" onClick={evaluateDecision} disabled={loading || !targetAgentId}>Evaluate Decision</Button>
                    <Button variant="contained" onClick={executeHandoff} disabled={loading || !targetAgentId}>Execute Handoff</Button>
                  </Stack>

                  {allowedTargets.length > 0 && (
                    <Box>
                      <Typography variant="caption" color="text.secondary">Allowed targets</Typography>
                      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                        {allowedTargets.map((t) => <Chip key={t} label={t} size="small" />)}
                      </Stack>
                    </Box>
                  )}

                  {policyDecision && (
                    <Alert severity={policyDecision?.isAllowed ? 'success' : 'warning'}>
                      Decision: <strong>{policyDecision?.isAllowed ? 'ALLOWED' : 'BLOCKED'}</strong>
                      {policyDecision?.reason ? ` · ${policyDecision.reason}` : ''}
                    </Alert>
                  )}

                  {handoffResult && (
                    <Alert severity={handoffResult?.ok ? 'success' : 'error'}>
                      Handoff result: {handoffResult?.ok ? 'OK' : 'FAILED'}
                    </Alert>
                  )}
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} lg={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>Live Session Ownership</Typography>
                <Box sx={{ height: 360 }}>
                  <DataGrid
                    rows={sessions}
                    columns={sessionColumns}
                    loading={loading}
                    getRowId={(r) => r.id}
                    onRowClick={(p) => { void inspectSession(p.row as SessionRow); }}
                    pageSizeOptions={[10, 20, 50]}
                    initialState={{ pagination: { paginationModel: { pageSize: 10 } } }}
                  />
                </Box>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Session Execution Timeline {selectedSession ? `· ${selectedSession.id}` : ''}
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Shows execution chain for selected session (manager/sub-agent ownership trace).
                </Typography>
                <Box sx={{ height: 320 }}>
                  <DataGrid
                    rows={sessionExecutions}
                    columns={execColumns}
                    getRowId={(r) => r.id}
                    pageSizeOptions={[10, 20, 50]}
                    initialState={{ pagination: { paginationModel: { pageSize: 10 } } }}
                  />
                </Box>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
