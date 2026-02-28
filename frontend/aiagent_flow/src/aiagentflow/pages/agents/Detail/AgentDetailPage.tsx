import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { useRouter , useParams } from 'src/routes/hooks';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

const statusColor = (status: string) => {
  switch (status) {
    case 'Published':
      return 'success';
    case 'Draft':
      return 'warning';
    case 'Archived':
      return 'error';
    default:
      return 'default';
  }
};

// ----------------------------------------------------------------------

export default function AgentDetailPage() {
  const theme = useTheme();
  const router = useRouter();
  const { id } = useParams();
  const tenantId = useTenantId();
  const [agent, setAgent] = useState<any>(null);
  const [executions, setExecutions] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [currentTab, setCurrentTab] = useState('overview');
  const [allowedTargets, setAllowedTargets] = useState<string[]>([]);
  const [allowlistVisible, setAllowlistVisible] = useState(false);
  const [targetAgentInput, setTargetAgentInput] = useState('');
  const [decisionLoading, setDecisionLoading] = useState(false);
  const [policyDecision, setPolicyDecision] = useState<{allowed:boolean; reason:string; hasExplicitPolicy:boolean} | null>(null);
  const [candidateTargets, setCandidateTargets] = useState<string[]>([]);

  useEffect(() => {
    const fetchAgentDetail = async () => {
      try {
        setLoading(true);
        const response = await axios.get(`/api/v1/tenants/${tenantId}/agents/${id}`);
        setAgent(response.data);

        // Fetch recent executions
        const execResponse = await axios.get(
          `/api/v1/tenants/${tenantId}/agents/${id}/executions?limit=10`
        );
        setExecutions(execResponse.data);

        // Fetch agent catalog for target selection
        try {
          const agentsResponse = await axios.get(`/api/v1/tenants/${tenantId}/agents`);
          const targets = (agentsResponse.data ?? [])
            .filter((a: any) => a?.id && a.id !== id)
            .filter((a: any) => a?.status !== 'Archived')
            .map((a: any) => a.id as string);
          setCandidateTargets(targets);
        } catch {
          setCandidateTargets([]);
        }

        // Fetch manager handoff allowlist (may be forbidden based on permissions)
        try {
          const allowResponse = await axios.get(
            endpoints.agentflow.executions.handoffAllowedTargets(tenantId, id as string)
          );
          setAllowedTargets(allowResponse.data?.targets ?? []);
          setAllowlistVisible(true);
        } catch {
          setAllowedTargets([]);
          setAllowlistVisible(false);
        }
      } catch (error) {
        console.error('Failed to fetch agent details:', error);
      } finally {
        setLoading(false);
      }
    };

    if (id) {
      fetchAgentDetail();
    }
  }, [id, tenantId]);

  const handleEdit = () => {
    router.push(`${paths.dashboard.agentDesigner}?id=${id}`);
  };

  const handleBack = () => {
    router.back();
  };


  const evaluateDecision = async (target?: string) => {
    const targetValue = (target ?? targetAgentInput).trim();

    if (!id || !targetValue) {
      setPolicyDecision(null);
      return;
    }

    try {
      setDecisionLoading(true);
      const response = await axios.get(
        endpoints.agentflow.executions.handoffDecision(tenantId, id as string, targetValue)
      );
      setPolicyDecision({
        allowed: !!response.data?.allowed,
        reason: response.data?.reason ?? 'unknown',
        hasExplicitPolicy: !!response.data?.hasExplicitPolicy,
      });
    } catch (error) {
      console.error('Failed to evaluate handoff decision:', error);
      setPolicyDecision({ allowed: false, reason: 'evaluation_failed', hasExplicitPolicy: false });
    } finally {
      setDecisionLoading(false);
    }
  };

  if (loading) {
    return (
      <DashboardContent maxWidth="xl">
        <LinearProgress />
      </DashboardContent>
    );
  }

  if (!agent) {
    return (
      <DashboardContent maxWidth="xl">
        <Box sx={{ textAlign: 'center', py: 10 }}>
          <Typography variant="h6" color="text.secondary">
            Agent not found
          </Typography>
          <Button variant="outlined" onClick={handleBack} sx={{ mt: 2 }}>
            Go Back
          </Button>
        </Box>
      </DashboardContent>
    );
  }

  return (
    <>
      <Helmet>
        <title>{agent.name} | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        {/* Header */}
        <Box sx={{ mb: 5 }}>
          <Button
            startIcon={<Iconify icon="eva:arrow-back-fill" />}
            onClick={handleBack}
            sx={{ mb: 2 }}
          >
            Back to Agents
          </Button>

          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
            <Box sx={{ flexGrow: 1 }}>
              <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 1 }}>
                <Typography variant="h4">{agent.name}</Typography>
                <Label color={statusColor(agent.status)}>{agent.status}</Label>
                <Chip label={`v${agent.version}`} size="small" variant="outlined" />
              </Stack>
              <Typography variant="body2" color="text.secondary">
                {agent.description || 'No description provided'}
              </Typography>
            </Box>

            <Stack direction="row" spacing={1}>
              <Button
                variant="outlined"
                startIcon={<Iconify icon="solar:pen-bold" />}
                onClick={handleEdit}
              >
                Edit
              </Button>
              <Button
                variant="contained"
                startIcon={<Iconify icon="solar:play-bold" />}
                onClick={() => {
                  // TODO: Trigger execute dialog
                }}
              >
                Execute
              </Button>
            </Stack>
          </Box>
        </Box>

        {/* Tabs */}
        <Tabs value={currentTab} onChange={(e, value) => setCurrentTab(value)} sx={{ mb: 3 }}>
          <Tab label="Overview" value="overview" />
          <Tab label="Configuration" value="configuration" />
          <Tab label="Execution History" value="executions" />
        </Tabs>

        {/* Tab Panels */}
        {currentTab === 'overview' && (
          <Grid container spacing={3}>
            {/* Basic Info */}
            <Grid item xs={12} md={6}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Basic Information
                  </Typography>
                  <Divider sx={{ mb: 2 }} />
                  <Stack spacing={2}>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Agent ID
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {agent.id}
                      </Typography>
                    </Box>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Created At
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {new Date(agent.createdAt).toLocaleString()}
                      </Typography>
                    </Box>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Last Updated
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {new Date(agent.updatedAt).toLocaleString()}
                      </Typography>
                    </Box>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Tags
                      </Typography>
                      <Stack direction="row" spacing={1} sx={{ mt: 0.5 }}>
                        {agent.tags?.map((tag: string) => (
                          <Chip key={tag} label={tag} size="small" />
                        )) || <Typography variant="caption">No tags</Typography>}
                      </Stack>
                    </Box>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>

            {/* Agent Configuration Summary */}
            <Grid item xs={12} md={6}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Configuration Summary
                  </Typography>
                  <Divider sx={{ mb: 2 }} />
                  <Stack spacing={2}>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Brain Provider
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {agent.brain?.provider || 'Not configured'}
                      </Typography>
                    </Box>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Loop Strategy
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {agent.loop?.strategy || 'Not configured'}
                      </Typography>
                    </Box>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Memory Type
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {agent.memory?.type || 'None'}
                      </Typography>
                    </Box>
                    <Box>
                      <Typography variant="caption" color="text.secondary">
                        Available Tools
                      </Typography>
                      <Typography variant="body2" fontWeight={600}>
                        {agent.availableTools?.length || 0} tools
                      </Typography>
                    </Box>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>

            {/* Execution Stats */}
            <Grid item xs={12}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Execution Statistics
                  </Typography>
                  <Divider sx={{ mb: 2 }} />
                  <Grid container spacing={3}>
                    <Grid item xs={12} sm={3}>
                      <Box sx={{ textAlign: 'center' }}>
                        <Typography variant="h3" color="primary">
                          {executions.length}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          Total Executions
                        </Typography>
                      </Box>
                    </Grid>
                    <Grid item xs={12} sm={3}>
                      <Box sx={{ textAlign: 'center' }}>
                        <Typography variant="h3" color="success.main">
                          {executions.filter((e) => e.status === 'Completed').length}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          Successful
                        </Typography>
                      </Box>
                    </Grid>
                    <Grid item xs={12} sm={3}>
                      <Box sx={{ textAlign: 'center' }}>
                        <Typography variant="h3" color="error.main">
                          {executions.filter((e) => e.status === 'Failed').length}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          Failed
                        </Typography>
                      </Box>
                    </Grid>
                    <Grid item xs={12} sm={3}>
                      <Box sx={{ textAlign: 'center' }}>
                        <Typography variant="h3" color="warning.main">
                          {executions.filter((e) => e.status === 'Running').length}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          Running
                        </Typography>
                      </Box>
                    </Grid>
                  </Grid>
                </CardContent>
              </Card>
            </Grid>
          </Grid>
        )}

        {currentTab === 'configuration' && (
          <Stack spacing={3}>
            {allowlistVisible && (
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Manager Handoff Allowlist
                  </Typography>
                  <Divider sx={{ mb: 2 }} />
                  {allowedTargets.length === 0 ? (
                    <Typography variant="body2" color="text.secondary">
                      No explicit allowed targets configured for this manager agent.
                    </Typography>
                  ) : (
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      {allowedTargets.map((target) => (
                        <Chip key={target} label={target} color="primary" variant="outlined" sx={{ mb: 1 }} />
                      ))}
                    </Stack>
                  )}
                </CardContent>
              </Card>
            )}

            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Policy Decision Preview
                </Typography>
                <Divider sx={{ mb: 2 }} />
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'stretch', sm: 'center' }}>
                  <TextField
                    select
                    label="Target Subagent ID"
                    size="small"
                    value={targetAgentInput}
                    onChange={(e) => {
                      const value = e.target.value;
                      setTargetAgentInput(value);
                      void evaluateDecision(value);
                    }}
                    fullWidth
                    helperText={candidateTargets.length === 0 ? 'No target agents available' : 'Select a target to evaluate policy decision'}
                  >
                    {candidateTargets.map((target) => (
                      <MenuItem key={target} value={target}>
                        {target}
                      </MenuItem>
                    ))}
                  </TextField>
                  <Button variant="contained" onClick={() => evaluateDecision()} disabled={decisionLoading || !targetAgentInput.trim()}>
                    {decisionLoading ? 'Evaluating...' : 'Re-evaluate'}
                  </Button>
                </Stack>

                {policyDecision && (
                  <Alert severity={policyDecision.allowed ? 'success' : 'error'} sx={{ mt: 2 }}>
                    Decision: <strong>{policyDecision.allowed ? 'ALLOW' : 'DENY'}</strong> · reason: <strong>{policyDecision.reason}</strong>{' '}
                    ({policyDecision.hasExplicitPolicy ? 'explicit policy' : 'fallback/default'})
                  </Alert>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Full Configuration
                </Typography>
              <Divider sx={{ mb: 2 }} />
              <Box
                component="pre"
                sx={{
                  p: 2,
                  bgcolor: alpha(theme.palette.grey[500], 0.08),
                  borderRadius: 1,
                  overflow: 'auto',
                  fontSize: '0.875rem',
                }}
              >
                {JSON.stringify(agent, null, 2)}
              </Box>
            </CardContent>
          </Card>
          </Stack>
        )}

        {currentTab === 'executions' && (
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Recent Executions
              </Typography>
              <Divider sx={{ mb: 2 }} />
              {executions.length === 0 ? (
                <Box sx={{ textAlign: 'center', py: 5 }}>
                  <Typography variant="body2" color="text.secondary">
                    No executions yet
                  </Typography>
                </Box>
              ) : (
                <Stack spacing={2}>
                  {executions.map((execution) => (
                    <Card
                      key={execution.id}
                      variant="outlined"
                      sx={{
                        cursor: 'pointer',
                        '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.04) },
                      }}
                      onClick={() => router.push(`${paths.dashboard.executions}/${execution.id}`)}
                    >
                      <CardContent>
                        <Stack direction="row" justifyContent="space-between" alignItems="center">
                          <Box>
                            <Typography variant="subtitle2" gutterBottom>
                              Execution {execution.id}
                            </Typography>
                            <Typography variant="caption" color="text.secondary">
                              Started: {new Date(execution.startedAt).toLocaleString()}
                            </Typography>
                          </Box>
                          <Label color={statusColor(execution.status)}>{execution.status}</Label>
                        </Stack>
                      </CardContent>
                    </Card>
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>
        )}
      </DashboardContent>
    </>
  );
}
