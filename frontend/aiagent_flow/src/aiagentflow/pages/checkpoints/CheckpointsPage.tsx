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

  return (
    <Card
      sx={{
        position: 'relative',
        borderLeft: `4px solid ${theme.palette.warning.main}`,
        transition: 'all 0.2s ease',
        '&:hover': { boxShadow: theme.shadows[8], transform: 'translateY(-1px)' },
      }}
    >
      <CardContent sx={{ p: 3 }}>
        {/* Header */}
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" sx={{ mb: 2 }}>
          <Stack direction="row" alignItems="center" spacing={1.5}>
            <Avatar
              sx={{
                width: 44,
                height: 44,
                bgcolor: alpha(theme.palette.warning.main, 0.12),
                color: 'warning.dark',
              }}
            >
              <Iconify icon="mdi:account-clock" width={24} />
            </Avatar>
            <Box>
              <Typography variant="subtitle1" fontWeight={700}>
                Human Review Required
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {timeSince} · Execution {checkpoint.executionId.slice(0, 8)}…
              </Typography>
            </Box>
          </Stack>
          <Chip
            label="Pending"
            color="warning"
            variant="soft"
            size="small"
            icon={<Iconify icon="mdi:clock-outline" width={14} />}
          />
        </Stack>

        {/* Reason */}
        <Paper
          variant="outlined"
          sx={{
            p: 2,
            mb: 2,
            bgcolor: alpha(theme.palette.warning.main, 0.04),
            borderColor: alpha(theme.palette.warning.main, 0.2),
          }}
        >
          <Typography variant="subtitle2" sx={{ mb: 0.5, color: 'warning.dark' }}>
            <Iconify icon="mdi:alert-circle-outline" width={16} sx={{ mr: 0.5, verticalAlign: 'text-bottom' }} />
            Why does this need approval?
          </Typography>
          <Typography variant="body2">{checkpoint.reason}</Typography>
        </Paper>

        {/* Details */}
        <Stack spacing={1.5} sx={{ mb: 2.5 }}>
          {checkpoint.toolName && (
            <Stack direction="row" spacing={1} alignItems="center">
              <Iconify icon="mdi:wrench" width={18} sx={{ color: 'text.secondary' }} />
              <Typography variant="body2">
                Tool: <strong>{checkpoint.toolName}</strong>
              </Typography>
            </Stack>
          )}
          {checkpoint.llmRationale && (
            <Stack direction="row" spacing={1} alignItems="flex-start">
              <Iconify icon="mdi:brain" width={18} sx={{ color: 'text.secondary', mt: 0.25 }} />
              <Box>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                  LLM Rationale
                </Typography>
                <Typography variant="body2" sx={{ fontStyle: 'italic' }}>
                  &quot;{checkpoint.llmRationale}&quot;
                </Typography>
              </Box>
            </Stack>
          )}
          {checkpoint.toolInputJson && (
            <Box>
              <Typography variant="caption" color="text.secondary" sx={{ mb: 0.5, display: 'block' }}>
                Proposed Tool Input
              </Typography>
              <Paper
                variant="outlined"
                sx={{
                  p: 1.5,
                  fontFamily: 'monospace',
                  fontSize: '0.75rem',
                  whiteSpace: 'pre-wrap',
                  maxHeight: 120,
                  overflow: 'auto',
                  bgcolor: alpha(theme.palette.grey[500], 0.04),
                }}
              >
                {formatJson(checkpoint.toolInputJson)}
              </Paper>
            </Box>
          )}
        </Stack>

        <Divider sx={{ mb: 2 }} />

        {/* Actions */}
        <Stack direction="row" spacing={1.5} justifyContent="flex-end">
          <Button
            variant="outlined"
            color="error"
            size="small"
            startIcon={deciding ? <CircularProgress size={14} /> : <Iconify icon="mdi:close-circle" />}
            disabled={deciding}
            onClick={() => onReject(checkpoint)}
            sx={{ px: 2.5 }}
          >
            Reject
          </Button>
          <Button
            variant="contained"
            color="success"
            size="small"
            startIcon={deciding ? <CircularProgress size={14} color="inherit" /> : <Iconify icon="mdi:check-circle" />}
            disabled={deciding}
            onClick={() => onApprove(checkpoint)}
            sx={{ px: 2.5 }}
          >
            Approve & Continue
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
