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
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { fetchCheckpoints, decideCheckpoint } from './checkpointSlice';

import type { Checkpoint } from './checkpointSlice';

// â”€â”€â”€ Checkpoint Card â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
                {isHighRisk ? 'Autorizacion critica' : 'Revision de accion requerida'}
                {isHighRisk && (
                    <Chip label="Alto riesgo" size="small" color="error" variant="soft" sx={{ fontSize: '0.65rem', height: 18, fontWeight: 800 }} />
                )}
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                <Iconify icon="mdi:clock-outline" width={14} />
                Recibido {timeSince} · TraceId: {checkpoint.executionId}
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
                Contexto de gobierno
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
                  <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>Metadata de tool</Typography>
                 <Stack spacing={1.5}>
                    <Stack direction="row" spacing={1} alignItems="center">
                        <Badge overlap="circular" anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }} 
                               badgeContent={<Iconify icon="mdi:server-network" sx={{ color: 'primary.main', bgcolor: 'background.paper', borderRadius: '50%' }} width={12} />}>
                            <Iconify icon="mdi:application-cog-outline" width={20} sx={{ color: 'text.secondary' }} />
                        </Badge>
                        <Typography variant="body2">Tool: <strong>{checkpoint.toolName || 'Flujo general'}</strong></Typography>
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
                  <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>Inspeccion de payload</Typography>
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
                        <Typography variant="caption" color="text.disabled">No se proporciono payload.</Typography>
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
            Rechazar acceso
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
            {isHighRisk ? 'Autorizar accion critica' : 'Aprobar y continuar'}
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

// â”€â”€ Helpers â”€â”€

function getTimeSince(dateStr: string): string {
  const diff = Date.now() - new Date(dateStr).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'justo ahora';
  if (mins < 60) return `hace ${mins}m`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `hace ${hrs}h`;
  return `hace ${Math.floor(hrs / 24)}d`;
}

function formatJson(str: string): string {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
}

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// CHECKPOINTS PAGE
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

export default function CheckpointsPage() {
  const dispatch = useDispatch<AppDispatch>();
  const tenantId = useTenantId();
  const theme = useTheme();
  const { items, loading, decidingId, error } = useSelector(
    (state: RootState) => state.checkpoints
  );

  const [rejectDialog, setRejectDialog] = useState<Checkpoint | null>(null);
  const [rejectFeedback, setRejectFeedback] = useState('');

  useEffect(() => {
    dispatch(fetchCheckpoints(tenantId));
    // Auto-refresh every 15 seconds
    const interval = setInterval(() => {
      dispatch(fetchCheckpoints(tenantId));
    }, 15000);
    return () => clearInterval(interval);
  }, [dispatch, tenantId]);

  const handleApprove = (cp: Checkpoint) => {
    dispatch(decideCheckpoint({
      tenantId: cp.tenantId,
      executionId: cp.executionId,
      checkpointId: cp.checkpointId,
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
        checkpointId: rejectDialog.checkpointId,
        approved: false,
        feedback: rejectFeedback || 'Rejected by human reviewer.',
      }));
      setRejectDialog(null);
    }
  };

  return (
    <>
      <Helmet>
        <title>Cola de revision | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        <Paper
          variant="outlined"
          sx={{
            mb: 3,
            p: { xs: 2.5, md: 3 },
            borderRadius: 4,
            background:
              'radial-gradient(circle at 8% 18%, rgba(255,171,0,0.16), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ md: 'center' }}>
            <Stack direction="row" alignItems="center" spacing={1.5}>
              <Avatar sx={{ width: 56, height: 56, bgcolor: 'warning.lighter', color: 'warning.dark' }}>
                <Badge badgeContent={items.length} color="warning" max={99}>
                  <Iconify icon="mdi:account-supervisor-outline" width={30} />
                </Badge>
              </Avatar>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Human in the loop
                </Typography>
                <Typography variant="h3">Revision humana</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                  Aprueba o rechaza acciones sensibles de agentes y workflows antes de que continuen.
                </Typography>
              </Box>
            </Stack>
            <Button
              variant="outlined"
              size="small"
              startIcon={<Iconify icon="mdi:refresh" />}
              onClick={() => dispatch(fetchCheckpoints(tenantId))}
            >
              Actualizar
            </Button>
          </Stack>
        </Paper>

        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 3 }}>
          <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Pendientes</Typography>
            <Typography variant="h4">{items.length}</Typography>
          </Card>
          <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Criticas</Typography>
            <Typography variant="h4">
              {
                items.filter(
                  (item) =>
                    item.reason.toLowerCase().includes('high') ||
                    item.reason.toLowerCase().includes('security') ||
                    item.toolName?.toLowerCase().includes('delete') ||
                    item.toolName?.toLowerCase().includes('admin')
                ).length
              }
            </Typography>
          </Card>
          <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Auto refresh</Typography>
            <Typography variant="h4">15s</Typography>
          </Card>
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
              Todo en orden
            </Typography>
            <Typography variant="body2" color="text.secondary">
              No hay revisiones pendientes. Las ejecuciones van correctamente.
            </Typography>
          </Paper>
        )}

        {/* Loading */}
        {loading && items.length === 0 && (
          <Box sx={{ textAlign: 'center', py: 6 }}>
            <CircularProgress />
            <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
              Cargando revisiones pendientes...
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
          Rechazar ejecucion
        </DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Indica por que esta accion se rechaza. La ejecucion quedara como fallida.
          </Typography>
          <TextField
            autoFocus
            fullWidth
            multiline
            rows={3}
            label="Motivo de rechazo"
            placeholder="Explica por que esta accion no debe continuar..."
            value={rejectFeedback}
            onChange={(e) => setRejectFeedback(e.target.value)}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setRejectDialog(null)}>Cancelar</Button>
          <Button variant="contained" color="error" onClick={confirmReject}>
            Confirmar rechazo
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

