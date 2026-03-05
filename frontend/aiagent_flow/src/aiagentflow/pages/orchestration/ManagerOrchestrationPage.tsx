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
  channelType?: string;
  identifier?: string;
  agentId?: string;
  status?: string;
  messageCount?: number;
};
type ExecutionRow = {
  id: string;
  status: string;
  createdAt: string;
  totalSteps?: number;
  totalTokensUsed?: number;
  correlationId?: string;
};
type DecisionRow = {
  id: string;
  intent: string;
  targetAgentId: string;
  allowed: boolean;
  reason: string;
  hasExplicitPolicy: boolean;
};

const DEFAULT_INTENTS = ['sales', 'support', 'billing', 'collections', 'reservations'];

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

  const [routingIntentsText, setRoutingIntentsText] = useState(DEFAULT_INTENTS.join(','));
  const [routingMatrix, setRoutingMatrix] = useState<DecisionRow[]>([]);
  const [managerA, setManagerA] = useState('');
  const [managerB, setManagerB] = useState('');
  const [managerATargets, setManagerATargets] = useState<string[]>([]);
  const [managerBTargets, setManagerBTargets] = useState<string[]>([]);

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

        const list: Agent[] = (agentsRes.data ?? [])
          .filter((a: any) => a?.id)
          .map((a: any) => ({ id: a.id, name: a.name }));

        setAgents(list);
        setSessions((sessionsRes.data ?? []) as SessionRow[]);

        if (!sourceAgentId && list.length > 0) setSourceAgentId(list[0].id);
        if (!managerA && list.length > 0) setManagerA(list[0].id);
        if (!managerB && list.length > 1) setManagerB(list[1].id);
      } catch (e: any) {
        setMessage(e?.message ?? 'Failed to load orchestration data');
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [tenantId]);

  const availableTargets = useMemo(() => agents.filter((a) => a.id !== sourceAgentId), [agents, sourceAgentId]);

  const loadAllowedTargets = async (managerId = sourceAgentId) => {
    if (!managerId) return [] as string[];
    const res = await axios.get(endpoints.agentflow.executions.handoffAllowedTargets(tenantId, managerId));
    return (res.data?.allowedTargets ?? res.data?.targets ?? []) as string[];
  };

  const refreshAllowedTargets = async () => {
    try {
      const targets = await loadAllowedTargets(sourceAgentId);
      setAllowedTargets(targets);
      setMessage(`Manager ${sourceAgentId} can use ${targets.length} sub-agents.`);
    } catch (e: any) {
      setAllowedTargets([]);
      setMessage(e?.message ?? 'Failed to load allowed targets');
    }
  };

  const evaluateDecision = async () => {
    if (!sourceAgentId || !targetAgentId) return;
    try {
      const res = await axios.get(
        endpoints.agentflow.executions.handoffDecision(tenantId, sourceAgentId, targetAgentId)
      );
      setPolicyDecision(res.data);
    } catch (e: any) {
      setPolicyDecision(null);
      setMessage(e?.message ?? 'Failed to evaluate policy decision');
    }
  };

  const executeHandoff = async () => {
    if (!sourceAgentId || !targetAgentId || !sessionId || !intent) return;
    try {
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
        metadata: { initiatedFrom: 'manager-orchestration-ui' },
      });

      setHandoffResult(res.data);
      setMessage('Handoff executed');
    } catch (e: any) {
      setHandoffResult(null);
      setMessage(e?.message ?? 'Handoff failed');
    }
  };

  const buildRoutingMatrix = async () => {
    if (!sourceAgentId) return;
    try {
      const intents = routingIntentsText.split(',').map((x) => x.trim()).filter(Boolean);
      const targets = await loadAllowedTargets(sourceAgentId);
      setAllowedTargets(targets);

      const rows: DecisionRow[] = [];
      for (const intentName of intents) {
        for (const target of targets) {
          try {
            const res = await axios.get(
              endpoints.agentflow.executions.handoffDecision(tenantId, sourceAgentId, target)
            );
            rows.push({
              id: `${intentName}-${target}`,
              intent: intentName,
              targetAgentId: target,
              allowed: !!res.data?.allowed,
              reason: res.data?.reason ?? 'unknown',
              hasExplicitPolicy: !!res.data?.hasExplicitPolicy,
            });
          } catch {
            rows.push({
              id: `${intentName}-${target}`,
              intent: intentName,
              targetAgentId: target,
              allowed: false,
              reason: 'decision_error',
              hasExplicitPolicy: false,
            });
          }
        }
      }

      setRoutingMatrix(rows);
      setMessage('Routing matrix evaluated. Use this to define intent→sub-agent mapping.');
    } catch (e: any) {
      setMessage(e?.message ?? 'Failed to build routing matrix');
    }
  };

  const loadSharedSubagents = async () => {
    if (!managerA || !managerB) return;
    try {
      const [aTargets, bTargets] = await Promise.all([loadAllowedTargets(managerA), loadAllowedTargets(managerB)]);
      setManagerATargets(aTargets);
      setManagerBTargets(bTargets);
    } catch (e: any) {
      setMessage(e?.message ?? 'Failed to compare managers');
    }
  };

  const sharedTargets = useMemo(
    () => managerATargets.filter((t) => managerBTargets.includes(t)),
    [managerATargets, managerBTargets]
  );

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
    { field: 'id', headerName: 'Session', flex: 1, minWidth: 180 },
    { field: 'channelType', headerName: 'Channel', width: 120 },
    { field: 'identifier', headerName: 'Identifier', flex: 1, minWidth: 140 },
    { field: 'agentId', headerName: 'Owner Agent', width: 180 },
    { field: 'messageCount', headerName: 'Msgs', width: 90, type: 'number' },
    { field: 'status', headerName: 'Status', width: 110 },
  ];

  const execColumns: GridColDef[] = [
    { field: 'id', headerName: 'Execution', flex: 1, minWidth: 180 },
    { field: 'status', headerName: 'Status', width: 110 },
    { field: 'createdAt', headerName: 'Created', width: 170, valueFormatter: (v) => (v ? new Date(v as string).toLocaleString() : '') },
    { field: 'totalSteps', headerName: 'Steps', width: 90, type: 'number' },
    { field: 'totalTokensUsed', headerName: 'Tokens', width: 100, type: 'number' },
    { field: 'correlationId', headerName: 'Correlation', flex: 1, minWidth: 160 },
  ];

  const routingColumns: GridColDef[] = [
    { field: 'intent', headerName: 'Intent', width: 140 },
    { field: 'targetAgentId', headerName: 'Target Sub-agent', flex: 1, minWidth: 180 },
    {
      field: 'allowed',
      headerName: 'Allowed',
      width: 100,
      renderCell: (p) => <Chip size="small" label={p.value ? 'YES' : 'NO'} color={p.value ? 'success' : 'error'} />,
    },
    { field: 'reason', headerName: 'Reason', width: 180 },
    {
      field: 'hasExplicitPolicy',
      headerName: 'Policy',
      width: 140,
      renderCell: (p) => <Chip size="small" label={p.value ? 'Explicit' : 'Default'} color={p.value ? 'primary' : 'default'} />,
    },
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
            Multi-subagent control center: allowlists, intent routing matrix, shared sub-agents and handoff simulation.
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
                    <Button variant="outlined" onClick={refreshAllowedTargets} disabled={loading}>Allowed Targets</Button>
                    <Button variant="outlined" onClick={evaluateDecision} disabled={!targetAgentId}>Evaluate Decision</Button>
                    <Button variant="contained" onClick={executeHandoff} disabled={!targetAgentId}>Execute Handoff</Button>
                  </Stack>
                  {allowedTargets.length > 0 && (
                    <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                      {allowedTargets.map((t) => <Chip key={t} label={t} size="small" color="primary" variant="outlined" />)}
                    </Stack>
                  )}
                  {policyDecision && (
                    <Alert severity={policyDecision?.allowed ? 'success' : 'warning'}>
                      Decision: <strong>{policyDecision?.allowed ? 'ALLOW' : 'DENY'}</strong> · {policyDecision?.reason}
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
                <Typography variant="h6" sx={{ mb: 2 }}>Shared Sub-agents Across Managers</Typography>
                <Stack spacing={2}>
                  <TextField select label="Manager A" value={managerA} onChange={(e) => setManagerA(e.target.value)}>
                    {agents.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                  </TextField>
                  <TextField select label="Manager B" value={managerB} onChange={(e) => setManagerB(e.target.value)}>
                    {agents.map((a) => <MenuItem key={a.id} value={a.id}>{a.name} ({a.id})</MenuItem>)}
                  </TextField>
                  <Button variant="outlined" onClick={loadSharedSubagents}>Compare Managers</Button>

                  <Typography variant="caption" color="text.secondary">Sub-agents that both managers can use:</Typography>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    {sharedTargets.length === 0 ? <Chip label="None" size="small" /> : sharedTargets.map((t) => <Chip key={t} label={t} size="small" color="success" />)}
                  </Stack>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>Intent Routing Matrix (Explainer)</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  This matrix makes explicit how a manager should select sub-agents by intent. Use it as operational mapping.
                </Typography>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
                  <TextField
                    fullWidth
                    label="Intents (comma separated)"
                    value={routingIntentsText}
                    onChange={(e) => setRoutingIntentsText(e.target.value)}
                  />
                  <Button variant="contained" onClick={buildRoutingMatrix}>Evaluate Matrix</Button>
                </Stack>
                <Box sx={{ height: 300 }}>
                  <DataGrid rows={routingMatrix} columns={routingColumns} getRowId={(r) => r.id} pageSizeOptions={[10, 25, 50]} />
                </Box>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} lg={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>Live Session Ownership</Typography>
                <Box sx={{ height: 320 }}>
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

          <Grid item xs={12} lg={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Session Execution Timeline {selectedSession ? `· ${selectedSession.id}` : ''}
                </Typography>
                <Box sx={{ height: 320 }}>
                  <DataGrid rows={sessionExecutions} columns={execColumns} getRowId={(r) => r.id} pageSizeOptions={[10, 20, 50]} initialState={{ pagination: { paginationModel: { pageSize: 10 } } }} />
                </Box>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
