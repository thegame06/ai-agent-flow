import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useEffect } from 'react';
import { useParams } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Timeline from '@mui/lab/Timeline';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import TimelineDot from '@mui/lab/TimelineDot';
import TimelineItem from '@mui/lab/TimelineItem';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import TimelineContent from '@mui/lab/TimelineContent';
import { alpha, useTheme } from '@mui/material/styles';
import TimelineSeparator from '@mui/lab/TimelineSeparator';
import TimelineConnector from '@mui/lab/TimelineConnector';
import CircularProgress from '@mui/material/CircularProgress';
import TimelineOppositeContent from '@mui/lab/TimelineOppositeContent';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Iconify } from 'src/components/iconify';

import { clearDetail, fetchExecutionDetail } from './executionDetailSlice';

import type { ExecutionStep } from './executionDetailSlice';

// ── Step type metadata ──

const STEP_META: Record<string, { icon: string; color: string; label: string }> = {
  think: { icon: 'mdi:head-lightbulb', color: '#7C4DFF', label: 'Think' },
  plan: { icon: 'mdi:map-outline', color: '#00BCD4', label: 'Plan' },
  act: { icon: 'mdi:lightning-bolt', color: '#FF9800', label: 'Act' },
  observe: { icon: 'mdi:eye-outline', color: '#4CAF50', label: 'Observe' },
  decide: { icon: 'mdi:source-branch', color: '#E91E63', label: 'Decide' },
  tool_call: { icon: 'mdi:wrench-outline', color: '#607D8B', label: 'Tool Call' },
  human_review: { icon: 'mdi:account-check', color: '#795548', label: 'Human Review' },
};

function getStepMeta(type: string) {
  return STEP_META[type] || { icon: 'mdi:help-circle', color: '#9E9E9E', label: type };
}

// ── Status Chip ──

function StatusChip({ status }: { status: string }) {
  const colors: Record<string, 'success' | 'warning' | 'error' | 'info' | 'default'> = {
    Completed: 'success',
    Running: 'info',
    Failed: 'error',
    HumanReviewPending: 'warning',
  };
  return <Chip label={status} size="small" color={colors[status] || 'default'} variant="soft" />;
}

// ── JSON viewer ──

function JsonViewer({ data, label }: { data?: string; label: string }) {
  const theme = useTheme();
  if (!data) return null;

  let formatted: string;
  try {
    formatted = JSON.stringify(JSON.parse(data), null, 2);
  } catch {
    formatted = data;
  }

  return (
    <Box sx={{ mt: 1 }}>
      <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>
        {label}
      </Typography>
      <Paper
        variant="outlined"
        sx={{
          mt: 0.5,
          p: 1.5,
          fontFamily: '"JetBrains Mono", "Fira Code", monospace',
          fontSize: '0.72rem',
          lineHeight: 1.6,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-all',
          maxHeight: 200,
          overflow: 'auto',
          bgcolor: alpha(theme.palette.grey[500], 0.04),
          borderColor: alpha(theme.palette.grey[500], 0.16),
        }}
      >
        {formatted}
      </Paper>
    </Box>
  );
}

// ── Decision Trace Step ──

function DecisionTraceStep({ step, isLast }: { step: ExecutionStep; isLast: boolean }) {
  const theme = useTheme();
  const meta = getStepMeta(step.type);
  const statusColor = step.status === 'Failed' ? 'error.main' : step.status === 'Running' ? 'info.main' : 'text.disabled';

  return (
    <TimelineItem>
      <TimelineOppositeContent sx={{ flex: 0.15, pt: 2.2 }}>
        <Typography variant="caption" color="text.disabled" sx={{ fontFamily: 'monospace' }}>
          {step.durationMs}ms
        </Typography>
        <Typography variant="caption" display="block" color="text.disabled">
          Step {step.stepNumber}
        </Typography>
      </TimelineOppositeContent>

      <TimelineSeparator>
        <TimelineDot
          sx={{
            bgcolor: meta.color,
            boxShadow: `0 0 0 4px ${alpha(meta.color, 0.16)}`,
            width: 36,
            height: 36,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <Iconify icon={meta.icon} width={18} sx={{ color: '#fff' }} />
        </TimelineDot>
        {!isLast && <TimelineConnector sx={{ bgcolor: alpha(theme.palette.grey[500], 0.16) }} />}
      </TimelineSeparator>

      <TimelineContent sx={{ pb: 3 }}>
        <Paper
          variant="outlined"
          sx={{
            p: 2,
            borderLeft: `3px solid ${meta.color}`,
            transition: 'all 0.2s',
            '&:hover': { boxShadow: theme.shadows[4], borderColor: meta.color },
          }}
        >
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Stack direction="row" alignItems="center" spacing={1}>
              <Typography variant="subtitle2" fontWeight={700}>
                {meta.label}
              </Typography>
              {step.toolName && (
                <Chip
                  label={step.toolName}
                  size="small"
                  variant="outlined"
                  icon={<Iconify icon="mdi:wrench" width={14} />}
                  sx={{ height: 22, fontSize: '0.7rem' }}
                />
              )}
            </Stack>
            <Typography variant="caption" sx={{ color: statusColor, fontWeight: 600 }}>
              {step.status}
            </Typography>
          </Stack>

          {step.rationale && (
            <Paper
              sx={{
                mt: 1.5,
                p: 1.5,
                bgcolor: alpha(meta.color, 0.04),
                borderRadius: 1,
                border: `1px solid ${alpha(meta.color, 0.12)}`,
              }}
            >
              <Stack direction="row" spacing={0.5} alignItems="flex-start">
                <Iconify icon="mdi:brain" width={16} sx={{ color: meta.color, mt: 0.25 }} />
                <Typography variant="body2" sx={{ fontStyle: 'italic', color: 'text.secondary' }}>
                  {step.rationale}
                </Typography>
              </Stack>
            </Paper>
          )}

          <JsonViewer data={step.inputJson} label="Input" />
          <JsonViewer data={step.outputJson} label="Output" />
        </Paper>
      </TimelineContent>
    </TimelineItem>
  );
}

// ══════════════════════════════════════════════════════════════════════════
// EXECUTION DETAIL PAGE
// ══════════════════════════════════════════════════════════════════════════

export default function ExecutionDetailPage() {
  const dispatch = useDispatch<AppDispatch>();
  const theme = useTheme();
  const { executionId } = useParams<{ executionId: string }>();
  const { detail, loading, error } = useSelector(
    (state: RootState) => state.executionDetail
  );

  useEffect(() => {
    if (executionId) {
      dispatch(fetchExecutionDetail({ tenantId: 'tenant-1', executionId }));
    }
    return () => { dispatch(clearDetail()); };
  }, [dispatch, executionId]);

  if (loading || !detail) {
    return (
      <DashboardContent maxWidth="lg">
        <Box sx={{ textAlign: 'center', py: 10 }}>
          <CircularProgress size={48} />
          <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
            Loading execution trace...
          </Typography>
        </Box>
      </DashboardContent>
    );
  }

  if (error) {
    return (
      <DashboardContent maxWidth="lg">
        <Alert severity="error">{error}</Alert>
      </DashboardContent>
    );
  }

  const successRate = detail.steps.filter((s) => s.status === 'Completed').length / Math.max(detail.steps.length, 1);

  return (
    <>
      <Helmet>
        <title>Execution {executionId?.slice(0, 8)}… | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        {/* Breadcrumb */}
        <Button
          component={RouterLink}
          href={paths.dashboard.executions}
          startIcon={<Iconify icon="mdi:arrow-left" />}
          sx={{ mb: 2, color: 'text.secondary' }}
        >
          Back to Executions
        </Button>

        {/* Header */}
        <Card sx={{ mb: 3 }}>
          <CardContent sx={{ p: 3 }}>
            <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
              <Stack direction="row" alignItems="center" spacing={2}>
                <Avatar
                  sx={{
                    width: 56,
                    height: 56,
                    bgcolor: alpha(theme.palette.primary.main, 0.08),
                    color: 'primary.main',
                    fontWeight: 800,
                    fontSize: '1.2rem',
                  }}
                >
                  {(detail.agentName || 'A').charAt(0).toUpperCase()}
                </Avatar>
                <Box>
                  <Stack direction="row" alignItems="center" spacing={1}>
                    <Typography variant="h5" fontWeight={800}>
                      {detail.agentName || detail.agentDefinitionId}
                    </Typography>
                    <StatusChip status={detail.status} />
                  </Stack>
                  <Typography variant="body2" color="text.secondary">
                    Execution ID: {detail.id}
                  </Typography>
                </Box>
              </Stack>

              {/* Metrics */}
              <Stack direction="row" spacing={3} divider={<Divider orientation="vertical" flexItem />}>
                <Tooltip title="Total Steps Executed">
                  <Box sx={{ textAlign: 'center' }}>
                    <Typography variant="h5" fontWeight={800} color="primary.main">
                      {detail.totalSteps}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">Steps</Typography>
                  </Box>
                </Tooltip>
                <Tooltip title="Total Duration">
                  <Box sx={{ textAlign: 'center' }}>
                    <Typography variant="h5" fontWeight={800} color="info.main">
                      {detail.durationMs}ms
                    </Typography>
                    <Typography variant="caption" color="text.secondary">Duration</Typography>
                  </Box>
                </Tooltip>
                <Tooltip title="Step Success Rate">
                  <Box sx={{ textAlign: 'center' }}>
                    <Typography
                      variant="h5"
                      fontWeight={800}
                      sx={{ color: successRate >= 0.8 ? 'success.main' : successRate >= 0.5 ? 'warning.main' : 'error.main' }}
                    >
                      {Math.round(successRate * 100)}%
                    </Typography>
                    <Typography variant="caption" color="text.secondary">Success</Typography>
                  </Box>
                </Tooltip>
                {detail.qualityScore !== undefined && (
                  <Tooltip title="LLM-Judge Quality Score">
                    <Box sx={{ textAlign: 'center' }}>
                      <Typography variant="h5" fontWeight={800} sx={{ color: 'secondary.main' }}>
                        {Math.round(detail.qualityScore * 100)}%
                      </Typography>
                      <Typography variant="caption" color="text.secondary">Quality</Typography>
                    </Box>
                  </Tooltip>
                )}
              </Stack>
            </Stack>

            {/* Failure Banner */}
            {detail.status === 'Failed' && detail.failureReason && (
              <Alert severity="error" sx={{ mt: 2 }} icon={<Iconify icon="mdi:alert-circle" />}>
                <strong>{detail.failureCode}:</strong> {detail.failureReason}
              </Alert>
            )}
          </CardContent>
        </Card>

        {/* Two columns: Input/Output + Decision Trace */}
        <Grid container spacing={3}>
          {/* Left: Input & Output */}
          <Grid item xs={12} md={4}>
            <Stack spacing={3}>
              {/* User Input */}
              <Card>
                <CardContent>
                  <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 2 }}>
                    <Iconify icon="mdi:account-voice" width={20} sx={{ color: 'primary.main' }} />
                    <Typography variant="subtitle1" fontWeight={700}>User Input</Typography>
                  </Stack>
                  <Paper variant="outlined" sx={{ p: 2, bgcolor: alpha(theme.palette.primary.main, 0.02) }}>
                    <Typography variant="body2">{detail.input.userMessage}</Typography>
                  </Paper>
                  <JsonViewer data={detail.input.contextJson} label="Context" />
                </CardContent>
              </Card>

              {/* Final Output */}
              {detail.output && (
                <Card>
                  <CardContent>
                    <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 2 }}>
                      <Iconify icon="mdi:message-check" width={20} sx={{ color: 'success.main' }} />
                      <Typography variant="subtitle1" fontWeight={700}>Final Response</Typography>
                    </Stack>
                    {detail.output.finalResponse && (
                      <Paper variant="outlined" sx={{ p: 2, bgcolor: alpha(theme.palette.success.main, 0.02) }}>
                        <Typography variant="body2">{detail.output.finalResponse}</Typography>
                      </Paper>
                    )}
                    <JsonViewer data={detail.output.outputJson} label="Structured Output" />
                  </CardContent>
                </Card>
              )}

              {/* Evaluation */}
              {detail.hallucinationRisk && (
                <Card>
                  <CardContent>
                    <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1.5 }}>
                      <Iconify icon="mdi:shield-search" width={20} sx={{ color: 'secondary.main' }} />
                      <Typography variant="subtitle1" fontWeight={700}>Evaluation</Typography>
                    </Stack>
                    <Stack spacing={1}>
                      <Stack direction="row" justifyContent="space-between">
                        <Typography variant="body2" color="text.secondary">Hallucination Risk</Typography>
                        <Chip
                          label={detail.hallucinationRisk}
                          size="small"
                          color={detail.hallucinationRisk === 'Low' ? 'success' : detail.hallucinationRisk === 'Medium' ? 'warning' : 'error'}
                          variant="soft"
                        />
                      </Stack>
                    </Stack>
                  </CardContent>
                </Card>
              )}
            </Stack>
          </Grid>

          {/* Right: Decision Trace Timeline */}
          <Grid item xs={12} md={8}>
            <Card>
              <CardContent>
                <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 2 }}>
                  <Iconify icon="mdi:timeline-clock-outline" width={22} sx={{ color: 'primary.main' }} />
                  <Typography variant="h6" fontWeight={700}>Decision Trace</Typography>
                  <Chip label={`${detail.steps.length} steps`} size="small" variant="soft" />
                </Stack>

                {detail.steps.length === 0 ? (
                  <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
                    <Iconify icon="mdi:timeline-question-outline" width={40} sx={{ color: 'text.disabled', mb: 1 }} />
                    <Typography variant="body2" color="text.secondary">
                      No steps recorded for this execution.
                    </Typography>
                  </Paper>
                ) : (
                  <Timeline position="right" sx={{ px: 0 }}>
                    {detail.steps.map((step, idx) => (
                      <DecisionTraceStep
                        key={`${step.stepNumber}-${idx}`}
                        step={step}
                        isLast={idx === detail.steps.length - 1}
                      />
                    ))}
                  </Timeline>
                )}
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
