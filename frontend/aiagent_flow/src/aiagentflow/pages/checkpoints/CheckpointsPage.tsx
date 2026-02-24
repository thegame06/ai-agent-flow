import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Paper from '@mui/material/Paper';
import Badge from '@mui/material/Badge';
import Button from '@mui/material/Button';
import Avatar from '@mui/material/Avatar';
import Dialog from '@mui/material/Dialog';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import DialogTitle from '@mui/material/DialogTitle';
import { alpha, useTheme } from '@mui/material/styles';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import CircularProgress from '@mui/material/CircularProgress';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Iconify } from 'src/components/iconify';

import { fetchCheckpoints, decideCheckpoint } from './checkpointSlice';

import type { Checkpoint } from './checkpointSlice';

// ─── Checkpoint Card ─────────────────────────────────────────────────────
function CheckpointCard({
  checkpoint,
  onApprove,
  onReject,
  deciding,
}: {
  checkpoint: Checkpoint;
  onApprove: (cp: Checkpoint) => void;
  onReject: (cp: Checkpoint) => void;
  deciding: boolean;
}) {
  const theme = useTheme();
  const timeSince = getTimeSince(checkpoint.createdAt);

  const isHighRisk =
    checkpoint.reason.toLowerCase().includes('high') ||
    checkpoint.reason.toLowerCase().includes('security') ||
    checkpoint.toolName?.toLowerCase().includes('sql') ||
    checkpoint.toolName?.toLowerCase().includes('delete') ||
    checkpoint.toolName?.toLowerCase().includes('admin');

  return (
    <Card
      sx={{
        position: 'relative',
        border: `1px solid ${alpha(isHighRisk ? theme.palette.error.main : theme.palette.divider, 0.2)}`,
        borderLeft: `6px solid ${isHighRisk ? theme.palette.error.main : theme.palette.warning.main}`,
        boxShadow: theme.customShadows.z1,
        transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        '&:hover': { 
            boxShadow: theme.customShadows.z8, 
            transform: 'translateX(4px)',
            borderColor: alpha(isHighRisk ? theme.palette.error.main : theme.palette.warning.main, 0.5)
        },
      }}
    >
      <CardContent sx={{ p: 3.5 }}>
        {/* Header Section */}
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" sx={{ mb: 3 }}>
          <Stack direction="row" alignItems="center" spacing={2}>
            <Avatar
              variant="rounded"
              sx={{
                width: 48,
                height: 48,
                bgcolor: alpha(isHighRisk ? theme.palette.error.main : theme.palette.warning.main, 0.1),
                color: isHighRisk ? 'error.main' : 'warning.main',
                border: `1px solid ${alpha(isHighRisk ? theme.palette.error.main : theme.palette.warning.main, 0.2)}`,
              }}
            >
              <Iconify icon={isHighRisk ? "mdi:shield-alert" : "mdi:file-eye"} width={28} />
            </Avatar>
            <Box>
              <Typography variant="h6" sx={{ color: isHighRisk ? 'error.dark' : 'text.primary', display: 'flex', alignItems: 'center', gap: 1 }}>
                {isHighRisk ? 'Critical Authorization' : 'Action Review Required'}
                {isHighRisk && (
                    <Chip label="High Risk" size="small" color="error" variant="soft" sx={{ fontSize: '0.65rem', height: 18, fontWeight: 800 }} />
                )}
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                <Iconify icon="mdi:clock-outline" width={14} />
                Received {timeSince} · TraceId: {checkpoint.executionId}
              </Typography>
            </Box>
          </Stack>
          
          <Stack spacing={0.5} alignItems="flex-end">
             <Chip
                label={checkpoint.tenantId}
                size="small"
                variant="outlined"
                sx={{ borderRadius: 1, borderColor: alpha(theme.palette.text.disabled, 0.3), fontWeight: 600 }}
              />
          </Stack>
        </Stack>

        {/* Security Justification */}
        <Box sx={{ mb: 3 }}>
             <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block', letterSpacing: 1 }}>
                Governance Context
             </Typography>
             <Paper
                variant="outlined"
                sx={{
                    p: 2,
                    borderStyle: 'dashed',
                    bgcolor: alpha(isHighRisk ? theme.palette.error.main : theme.palette.warning.main, 0.02),
                    borderColor: alpha(isHighRisk ? theme.palette.error.main : theme.palette.warning.main, 0.2),
                }}
            >
                <Typography variant="body2" sx={{ lineHeight: 1.6 }}>
                    <Iconify icon="mdi:information-variant-circle" width={18} sx={{ mr: 1, color: isHighRisk ? 'error.main' : 'warning.main', verticalAlign: 'middle' }} />
                    {checkpoint.reason}
                </Typography>
            </Paper>
        </Box>

        {/* Technical Drilldown */}
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={4} sx={{ mb: 3 }}>
            <Box sx={{ flex: 1 }}>
                 <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>Tool Metadata</Typography>
                 <Stack spacing={1.5}>
                    <Stack direction="row" spacing={1} alignItems="center">
                        <Badge overlap="circular" anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }} 
                               badgeContent={<Iconify icon="mdi:server-network" sx={{ color: 'primary.main', bgcolor: 'background.paper', borderRadius: '50%' }} width={12} />}>
                            <Iconify icon="mdi:application-cog-outline" width={20} sx={{ color: 'text.secondary' }} />
                        </Badge>
                        <Typography variant="body2">Tool: <strong>{checkpoint.toolName || 'General Flow'}</strong></Typography>
                    </Stack>
                    
                    {checkpoint.llmRationale && (
                        <Box sx={{ position: 'relative', pl: 3, borderLeft: `2px solid ${theme.palette.divider}` }}>
                            <Typography variant="caption" color="text.secondary" sx={{ position: 'absolute', left: -8, top: 4, bgcolor: 'background.paper', px: 0.5 }}>
                                <Iconify icon="mdi:brain" width={14} />
                            </Typography>
                            <Typography variant="body2" sx={{ color: 'text.secondary', fontStyle: 'italic', lineHeight: 1.5 }}>
                                &quot;{checkpoint.llmRationale}&quot;
                            </Typography>
                        </Box>
                    )}
                 </Stack>
            </Box>

            <Box sx={{ flex: 1.5 }}>
                  <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>Payload Inspection</Typography>
                  <Paper
                    sx={{
                      p: 2,
                      fontFamily: '"Fira Code", "Roboto Mono", monospace',
                      fontSize: '0.75rem',
                      whiteSpace: 'pre-wrap',
                      maxHeight: 180,
                      overflow: 'auto',
                      bgcolor: theme.palette.mode === 'dark' ? alpha(theme.palette.grey[900], 0.8) : '#f8f9fa',
                      border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
                      borderRadius: 1,
                      '&::-webkit-scrollbar': { width: 6, height: 6 },
                      '&::-webkit-scrollbar-thumb': { bgcolor: alpha(theme.palette.grey[500], 0.3), borderRadius: 3 },
                    }}
                  >
                    {checkpoint.toolInputJson ? (
                         <Box component="pre" sx={{ m: 0, color: theme.palette.primary.dark }}>
                             {formatJson(checkpoint.toolInputJson)}
                         </Box>
                    ) : (
                        <Typography variant="caption" color="text.disabled">No payload data provided.</Typography>
                    )}
                  </Paper>
            </Box>
        </Stack>

        <Divider sx={{ my: 3, borderStyle: 'dashed' }} />

        {/* Footer Actions */}
        <Stack direction="row" spacing={2} justifyContent="flex-end">
          <Button
            variant="soft"
            color="error"
            startIcon={deciding ? <CircularProgress size={16} /> : <Iconify icon="mdi:close-octagon" />}
            disabled={deciding}
            onClick={() => onReject(checkpoint)}
            sx={{ borderRadius: 1.2, fontWeight: 700 }}
          >
            Refuse Access
          </Button>
          <Button
            variant="contained"
            color={isHighRisk ? "error" : "success"}
            startIcon={deciding ? <CircularProgress size={16} color="inherit" /> : <Iconify icon="mdi:check-decagram" />}
            disabled={deciding}
            onClick={() => onApprove(checkpoint)}
            sx={{ 
                borderRadius: 1.2, 
                px: 3, 
                fontWeight: 700,
                boxShadow: isHighRisk ? theme.customShadows.error : theme.customShadows.success
            }}
          >
            {isHighRisk ? 'Authorize Critical Action' : 'Approve & Resume'}
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

// ── Helpers ──

function getTimeSince(dateStr: string): string {
  const diff = Date.now() - new Date(dateStr).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

function formatJson(str: string): string {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
}

// ══════════════════════════════════════════════════════════════════════════
// CHECKPOINTS PAGE
// ══════════════════════════════════════════════════════════════════════════

export default function CheckpointsPage() {
  const dispatch = useDispatch<AppDispatch>();
  const theme = useTheme();
  const { items, loading, decidingId, error } = useSelector(
    (state: RootState) => state.checkpoints
  );

  const [rejectDialog, setRejectDialog] = useState<Checkpoint | null>(null);
  const [rejectFeedback, setRejectFeedback] = useState('');

  useEffect(() => {
    dispatch(fetchCheckpoints('tenant-1'));
    // Auto-refresh every 15 seconds
    const interval = setInterval(() => {
      dispatch(fetchCheckpoints('tenant-1'));
    }, 15000);
    return () => clearInterval(interval);
  }, [dispatch]);

  const handleApprove = (cp: Checkpoint) => {
    dispatch(decideCheckpoint({
      tenantId: cp.tenantId,
      executionId: cp.executionId,
      approved: true,
    }));
  };

  const handleReject = (cp: Checkpoint) => {
    setRejectDialog(cp);
    setRejectFeedback('');
  };

  const confirmReject = () => {
    if (rejectDialog) {
      dispatch(decideCheckpoint({
        tenantId: rejectDialog.tenantId,
        executionId: rejectDialog.executionId,
        approved: false,
        feedback: rejectFeedback || 'Rejected by human reviewer.',
      }));
      setRejectDialog(null);
    }
  };

  return (
    <>
      <Helmet>
        <title>Review Queue | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        {/* Header */}
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 4 }}>
          <Box>
            <Stack direction="row" alignItems="center" spacing={1.5}>
              <Typography variant="h4" fontWeight={800}>
                Review Queue
              </Typography>
              <Badge badgeContent={items.length} color="warning" max={99}>
                <Iconify icon="mdi:bell-ring-outline" width={24} sx={{ color: 'text.secondary' }} />
              </Badge>
            </Stack>
            <Typography variant="body2" color="text.secondary">
              Approve or reject agent actions that require human verification.
            </Typography>
          </Box>
          <Button
            variant="outlined"
            size="small"
            startIcon={<Iconify icon="mdi:refresh" />}
            onClick={() => dispatch(fetchCheckpoints('tenant-1'))}
          >
            Refresh
          </Button>
        </Stack>

        {/* Error */}
        {error && (
          <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>
        )}

        {/* Empty State */}
        {!loading && items.length === 0 && (
          <Paper
            variant="outlined"
            sx={{
              p: 6,
              textAlign: 'center',
              borderStyle: 'dashed',
              bgcolor: alpha(theme.palette.success.main, 0.04),
              borderColor: alpha(theme.palette.success.main, 0.3),
            }}
          >
            <Iconify
              icon="mdi:check-decagram"
              width={64}
              sx={{ color: 'success.main', mb: 2 }}
            />
            <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>
              All Clear!
            </Typography>
            <Typography variant="body2" color="text.secondary">
              No pending reviews. All agent executions are running smoothly.
            </Typography>
          </Paper>
        )}

        {/* Loading */}
        {loading && items.length === 0 && (
          <Box sx={{ textAlign: 'center', py: 6 }}>
            <CircularProgress />
            <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
              Loading pending reviews...
            </Typography>
          </Box>
        )}

        {/* Checkpoint Cards */}
        <Stack spacing={3}>
          {items.map((checkpoint) => (
            <CheckpointCard
              key={checkpoint.checkpointId}
              checkpoint={checkpoint}
              onApprove={handleApprove}
              onReject={handleReject}
              deciding={decidingId === checkpoint.executionId}
            />
          ))}
        </Stack>
      </DashboardContent>

      {/* Reject Dialog */}
      <Dialog open={!!rejectDialog} onClose={() => setRejectDialog(null)} maxWidth="sm" fullWidth>
        <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify icon="mdi:alert" sx={{ color: 'error.main' }} />
          Reject Execution
        </DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Provide feedback on why this action is being rejected. The execution will be marked as failed.
          </Typography>
          <TextField
            autoFocus
            fullWidth
            multiline
            rows={3}
            label="Rejection Reason"
            placeholder="Explain why this action should not proceed..."
            value={rejectFeedback}
            onChange={(e) => setRejectFeedback(e.target.value)}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setRejectDialog(null)}>Cancel</Button>
          <Button variant="contained" color="error" onClick={confirmReject}>
            Confirm Rejection
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
