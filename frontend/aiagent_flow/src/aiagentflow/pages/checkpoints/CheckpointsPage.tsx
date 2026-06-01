import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useState, useEffect, useMemo } from 'react';
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
import ButtonBase from '@mui/material/ButtonBase';
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

type ReviewView = 'human' | 'technical';
type DecisionAction = 'reject' | 'fallback';

type DecisionDialogState = {
  checkpoint: Checkpoint;
  action: DecisionAction;
} | null;

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

function getCheckpointKind(checkpoint: Checkpoint): ReviewView {
  const explicitKind = checkpoint.context?.checkpointKind?.toLowerCase();
  if (explicitKind === 'technical') return 'technical';
  if (explicitKind === 'human') return 'human';

  const reason = checkpoint.reason.toLowerCase();
  if (
    reason.includes('routing to checkpoint') ||
    reason.includes('non-json') ||
    reason.includes('contract') ||
    reason.includes('parse')
  ) {
    return 'technical';
  }

  return 'human';
}

function isHighRiskCheckpoint(checkpoint: Checkpoint): boolean {
  const reason = checkpoint.reason.toLowerCase();
  const toolName = checkpoint.toolName?.toLowerCase() ?? '';
  return (
    reason.includes('high') ||
    reason.includes('security') ||
    reason.includes('critical') ||
    toolName.includes('sql') ||
    toolName.includes('delete') ||
    toolName.includes('admin')
  );
}

function getCheckpointTitle(checkpoint: Checkpoint): string {
  if (getCheckpointKind(checkpoint) === 'technical') {
    if (checkpoint.reason.toLowerCase().includes('non-json')) return 'Error de clasificacion';
    return 'Checkpoint tecnico';
  }

  return isHighRiskCheckpoint(checkpoint) ? 'Autorizacion critica requerida' : 'Revision humana requerida';
}

function getCheckpointPayload(checkpoint: Checkpoint): string | null {
  return checkpoint.context?.rawResponse || checkpoint.toolInputJson || null;
}

function getOriginNode(checkpoint: Checkpoint): string {
  return checkpoint.context?.originNode || checkpoint.toolName || 'Flujo general';
}

function getCorrelationId(checkpoint: Checkpoint): string | null {
  return checkpoint.context?.correlationId || null;
}

function getReviewCategory(checkpoint: Checkpoint): string | null {
  return checkpoint.context?.reviewCategory || null;
}

function getTechnicalHelper(checkpoint: Checkpoint): string {
  const issueCode = checkpoint.context?.issueCode;
  if (issueCode === 'maf.non_json_response') return 'El motor devolvio texto no estructurado cuando se esperaba JSON.';
  if (issueCode === 'maf.invalid_decision') return 'El motor devolvio una decision invalida para el contrato del router.';
  if (issueCode === 'maf.malformed_json') return 'La respuesta parecia JSON, pero no pudo parsearse correctamente.';
  return 'La ejecucion se desvio a checkpoint por una excepcion tecnica del flujo.';
}

function SectionCard({
  checkpoint,
  deciding,
  onApprove,
  onReject,
  onFallback,
}: {
  checkpoint: Checkpoint;
  deciding: boolean;
  onApprove: (cp: Checkpoint) => void;
  onReject: (cp: Checkpoint) => void;
  onFallback: (cp: Checkpoint) => void;
}) {
  const theme = useTheme();
  const kind = getCheckpointKind(checkpoint);
  const isHighRisk = isHighRiskCheckpoint(checkpoint);
  const timeSince = getTimeSince(checkpoint.createdAt);
  const payload = getCheckpointPayload(checkpoint);
  const correlationId = getCorrelationId(checkpoint);
  const reviewCategory = getReviewCategory(checkpoint);
  const technical = kind === 'technical';

  const accentColor = technical
    ? theme.palette.info.main
    : isHighRisk
      ? theme.palette.error.main
      : theme.palette.warning.main;

  return (
    <Card
      sx={{
        position: 'relative',
        border: `1px solid ${alpha(accentColor, 0.24)}`,
        borderLeft: `6px solid ${accentColor}`,
        boxShadow: theme.customShadows.z1,
        transition: 'all 0.25s ease',
        '&:hover': {
          boxShadow: theme.customShadows.z8,
          transform: 'translateX(3px)',
        },
      }}
    >
      <CardContent sx={{ p: 3.5 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" sx={{ mb: 3 }}>
          <Stack direction="row" spacing={2} alignItems="center">
            <Avatar
              variant="rounded"
              sx={{
                width: 48,
                height: 48,
                bgcolor: alpha(accentColor, 0.12),
                color: accentColor,
                border: `1px solid ${alpha(accentColor, 0.3)}`,
              }}
            >
              <Iconify
                icon={
                  technical
                    ? 'mdi:server-network-off'
                    : isHighRisk
                      ? 'mdi:shield-alert'
                      : 'mdi:account-supervisor-outline'
                }
                width={28}
              />
            </Avatar>

            <Box>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Typography variant="h6">{getCheckpointTitle(checkpoint)}</Typography>
                <Chip
                  size="small"
                  variant="soft"
                  color={technical ? 'info' : isHighRisk ? 'error' : 'warning'}
                  label={technical ? 'Tecnico' : 'Humano'}
                />
                {reviewCategory && (
                  <Chip size="small" variant="outlined" label={reviewCategory} />
                )}
              </Stack>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                <Iconify icon="mdi:clock-outline" width={14} />
                Recibido {timeSince} · ExecutionId: {checkpoint.executionId}
              </Typography>
            </Box>
          </Stack>

          <Chip
            label={checkpoint.tenantId}
            size="small"
            variant="outlined"
            sx={{ borderRadius: 1, fontWeight: 600 }}
          />
        </Stack>

        <Box sx={{ mb: 3 }}>
          <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block', letterSpacing: 1 }}>
            {technical ? 'Contexto tecnico' : 'Contexto de gobierno'}
          </Typography>
          <Paper
            variant="outlined"
            sx={{
              p: 2,
              borderStyle: 'dashed',
              bgcolor: alpha(accentColor, 0.03),
              borderColor: alpha(accentColor, 0.24),
            }}
          >
            <Typography variant="body2" sx={{ lineHeight: 1.6 }}>
              <Iconify icon="mdi:information-variant-circle" width={18} sx={{ mr: 1, color: accentColor, verticalAlign: 'middle' }} />
              {checkpoint.reason}
            </Typography>
            {technical && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.25 }}>
                {getTechnicalHelper(checkpoint)}
              </Typography>
            )}
          </Paper>
        </Box>

        <Stack direction={{ xs: 'column', lg: 'row' }} spacing={3} sx={{ mb: 3 }}>
          <Box sx={{ flex: 1 }}>
            <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>
              {technical ? 'Origen del checkpoint' : 'Metadata de revision'}
            </Typography>

            <Stack spacing={1.25}>
              <Stack direction="row" spacing={1} alignItems="center">
                <Iconify icon="mdi:source-branch" width={18} sx={{ color: 'text.secondary' }} />
                <Typography variant="body2">
                  Nodo origen: <strong>{getOriginNode(checkpoint)}</strong>
                </Typography>
              </Stack>

              {checkpoint.toolName && (
                <Stack direction="row" spacing={1} alignItems="center">
                  <Iconify icon="mdi:toolbox-outline" width={18} sx={{ color: 'text.secondary' }} />
                  <Typography variant="body2">
                    Tool: <strong>{checkpoint.toolName}</strong>
                  </Typography>
                </Stack>
              )}

              {correlationId && (
                <Stack direction="row" spacing={1} alignItems="center">
                  <Iconify icon="mdi:vector-link" width={18} sx={{ color: 'text.secondary' }} />
                  <Typography variant="body2">
                    CorrelationId: <strong>{correlationId}</strong>
                  </Typography>
                </Stack>
              )}

              {checkpoint.llmRationale && checkpoint.llmRationale !== checkpoint.reason && (
                <Box sx={{ position: 'relative', pl: 3, borderLeft: `2px solid ${theme.palette.divider}` }}>
                  <Typography
                    variant="caption"
                    color="text.secondary"
                    sx={{ position: 'absolute', left: -8, top: 4, bgcolor: 'background.paper', px: 0.5 }}
                  >
                    <Iconify icon="mdi:brain" width={14} />
                  </Typography>
                  <Typography variant="body2" sx={{ color: 'text.secondary', fontStyle: 'italic', lineHeight: 1.5 }}>
                    &quot;{checkpoint.llmRationale}&quot;
                  </Typography>
                </Box>
              )}
            </Stack>
          </Box>

          <Box sx={{ flex: 1.4 }}>
            <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>
              {technical ? 'Respuesta cruda / payload' : 'Inspeccion de payload'}
            </Typography>
            <Paper
              sx={{
                p: 2,
                fontFamily: '"Fira Code", "Roboto Mono", monospace',
                fontSize: '0.75rem',
                whiteSpace: 'pre-wrap',
                minHeight: 164,
                maxHeight: 220,
                overflow: 'auto',
                bgcolor: theme.palette.mode === 'dark' ? alpha(theme.palette.grey[900], 0.8) : '#f8f9fa',
                border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
                borderRadius: 1,
              }}
            >
              {payload ? (
                <Box component="pre" sx={{ m: 0, color: technical ? 'info.dark' : 'primary.dark' }}>
                  {formatJson(payload)}
                </Box>
              ) : (
                <Typography variant="caption" color="text.disabled">
                  No se proporciono payload.
                </Typography>
              )}
            </Paper>
          </Box>
        </Stack>

        <Divider sx={{ my: 3, borderStyle: 'dashed' }} />

        <Stack direction="row" spacing={1.5} justifyContent="flex-end" flexWrap="wrap" useFlexGap>
          {technical ? (
            <>
              <Button
                variant="soft"
                color="error"
                startIcon={deciding ? <CircularProgress size={16} /> : <Iconify icon="mdi:close-octagon" />}
                disabled={deciding}
                onClick={() => onReject(checkpoint)}
                sx={{ borderRadius: 1.2, fontWeight: 700 }}
              >
                Cancelar ejecucion
              </Button>
              <Button
                variant="outlined"
                color="warning"
                startIcon={deciding ? <CircularProgress size={16} /> : <Iconify icon="mdi:transit-connection-variant" />}
                disabled={deciding}
                onClick={() => onFallback(checkpoint)}
                sx={{ borderRadius: 1.2, fontWeight: 700 }}
              >
                Enviar a fallback
              </Button>
              <Button
                variant="contained"
                color="info"
                startIcon={deciding ? <CircularProgress size={16} color="inherit" /> : <Iconify icon="mdi:refresh-circle" />}
                disabled={deciding}
                onClick={() => onApprove(checkpoint)}
                sx={{ borderRadius: 1.2, px: 3, fontWeight: 700 }}
              >
                Reintentar
              </Button>
            </>
          ) : (
            <>
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
                color={isHighRisk ? 'error' : 'success'}
                startIcon={deciding ? <CircularProgress size={16} color="inherit" /> : <Iconify icon="mdi:check-decagram" />}
                disabled={deciding}
                onClick={() => onApprove(checkpoint)}
                sx={{ borderRadius: 1.2, px: 3, fontWeight: 700 }}
              >
                {isHighRisk ? 'Autorizar accion critica' : 'Aprobar y continuar'}
              </Button>
            </>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}

export default function CheckpointsPage() {
  const dispatch = useDispatch<AppDispatch>();
  const tenantId = useTenantId();
  const theme = useTheme();
  const { items, loading, decidingId, error } = useSelector(
    (state: RootState) => state.checkpoints
  );

  const [activeView, setActiveView] = useState<ReviewView>('human');
  const [decisionDialog, setDecisionDialog] = useState<DecisionDialogState>(null);
  const [decisionFeedback, setDecisionFeedback] = useState('');

  useEffect(() => {
    dispatch(fetchCheckpoints(tenantId));
    const interval = setInterval(() => {
      dispatch(fetchCheckpoints(tenantId));
    }, 15000);
    return () => clearInterval(interval);
  }, [dispatch, tenantId]);

  const grouped = useMemo(() => {
    const human: Checkpoint[] = [];
    const technical: Checkpoint[] = [];

    items.forEach((item) => {
      if (getCheckpointKind(item) === 'technical') technical.push(item);
      else human.push(item);
    });

    return { human, technical };
  }, [items]);

  useEffect(() => {
    if (activeView === 'human' && grouped.human.length === 0 && grouped.technical.length > 0) {
      setActiveView('technical');
    }
    if (activeView === 'technical' && grouped.technical.length === 0 && grouped.human.length > 0) {
      setActiveView('human');
    }
  }, [activeView, grouped.human.length, grouped.technical.length]);

  const visibleItems = activeView === 'human' ? grouped.human : grouped.technical;
  const criticalHumanCount = grouped.human.filter(isHighRiskCheckpoint).length;

  const sidebarSections: Array<{
    value: ReviewView;
    label: string;
    description: string;
    icon: string;
    count: number;
  }> = [
    {
      value: 'human',
      label: 'Revisiones humanas',
      description: 'Aprobaciones auditables para herramientas, riesgo o politicas.',
      icon: 'mdi:account-supervisor-outline',
      count: grouped.human.length,
    },
    {
      value: 'technical',
      label: 'Checkpoints tecnicos',
      description: 'Errores de contrato, parsing o control interno del flujo.',
      icon: 'mdi:server-network-off',
      count: grouped.technical.length,
    },
  ];

  const activeSection = sidebarSections.find((entry) => entry.value === activeView)!;

  const handleApprove = (cp: Checkpoint) => {
    dispatch(decideCheckpoint({
      tenantId: cp.tenantId,
      executionId: cp.executionId,
      checkpointId: cp.checkpointId,
      approved: true,
      action: 'approve',
    }));
  };

  const openDecisionDialog = (cp: Checkpoint, action: DecisionAction) => {
    setDecisionDialog({ checkpoint: cp, action });
    setDecisionFeedback('');
  };

  const confirmDecision = () => {
    if (!decisionDialog) return;

    const { checkpoint, action } = decisionDialog;
    const isFallback = action === 'fallback';

    dispatch(decideCheckpoint({
      tenantId: checkpoint.tenantId,
      executionId: checkpoint.executionId,
      checkpointId: checkpoint.checkpointId,
      approved: isFallback ? true : false,
      action,
      feedback:
        decisionFeedback ||
        (isFallback
          ? 'La ejecucion fue enviada a un flujo alterno despues de la revision tecnica.'
          : 'Rejected by reviewer.'),
    }));

    setDecisionDialog(null);
  };

  return (
    <>
      <Helmet>
        <title>Revisiones y checkpoints | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper
          variant="outlined"
          sx={{
            mb: 3,
            p: { xs: 2.5, md: 3 },
            borderRadius: 4,
            borderColor:
              theme.palette.mode === 'dark'
                ? alpha(theme.palette.warning.light, 0.24)
                : alpha(theme.palette.warning.main, 0.2),
            background:
              theme.palette.mode === 'dark'
                ? `radial-gradient(circle at 8% 18%, ${alpha(theme.palette.warning.main, 0.18)}, transparent 34%), linear-gradient(135deg, ${alpha(
                    theme.palette.background.paper,
                    0.96
                  )} 0%, ${alpha(theme.palette.grey[900], 0.9)} 100%)`
                : 'radial-gradient(circle at 8% 18%, rgba(255,171,0,0.16), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ md: 'center' }}>
            <Stack direction="row" alignItems="center" spacing={1.5}>
              <Avatar sx={{ width: 56, height: 56, bgcolor: 'warning.lighter', color: 'warning.dark' }}>
                <Badge badgeContent={items.length} color="warning" max={99}>
                  <Iconify icon="mdi:account-supervisor-circle-outline" width={30} />
                </Badge>
              </Avatar>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Trust and control
                </Typography>
                <Typography variant="h3">Revisiones y checkpoints</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                  Separa aprobaciones humanas auditables de excepciones tecnicas que requieren triage operativo.
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
            <Typography variant="subtitle2" color="text.secondary">Pendientes humanas</Typography>
            <Typography variant="h4">{grouped.human.length}</Typography>
          </Card>
          <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Pendientes tecnicas</Typography>
            <Typography variant="h4">{grouped.technical.length}</Typography>
          </Card>
          <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Criticas humanas</Typography>
            <Typography variant="h4">{criticalHumanCount}</Typography>
          </Card>
          <Card variant="outlined" sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Auto refresh</Typography>
            <Typography variant="h4">15s</Typography>
          </Card>
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>
        )}

        <Stack direction={{ xs: 'column', lg: 'row' }} spacing={2.5} alignItems="stretch">
          <Card
            variant="outlined"
            sx={{
              width: { xs: '100%', lg: 280 },
              minWidth: { lg: 280 },
              p: 1.5,
              borderRadius: 3,
              alignSelf: { lg: 'flex-start' },
            }}
          >
            <Stack spacing={0.75}>
              {sidebarSections.map((entry) => {
                const active = entry.value === activeView;
                return (
                  <ButtonBase
                    key={entry.value}
                    onClick={() => setActiveView(entry.value)}
                    sx={{
                      width: '100%',
                      px: 1.25,
                      py: 1.2,
                      borderRadius: 2,
                      justifyContent: 'flex-start',
                      textAlign: 'left',
                      bgcolor: active ? 'action.selected' : 'transparent',
                      borderLeft: '3px solid',
                      borderColor: active ? 'primary.main' : 'transparent',
                    }}
                  >
                    <Stack spacing={0.5} sx={{ width: '100%' }}>
                      <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
                        <Stack direction="row" spacing={1.1} alignItems="center">
                          <Iconify icon={entry.icon} width={18} />
                          <Typography variant="body2" fontWeight={active ? 700 : 600}>
                            {entry.label}
                          </Typography>
                        </Stack>
                        <Chip size="small" label={entry.count} color={active ? 'primary' : 'default'} />
                      </Stack>
                      <Typography variant="caption" color="text.secondary">
                        {entry.description}
                      </Typography>
                    </Stack>
                  </ButtonBase>
                );
              })}
            </Stack>
          </Card>

          <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, flex: 1, minWidth: 0 }}>
            <Stack spacing={1.5} sx={{ mb: 2.5 }}>
              <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5, bgcolor: 'background.neutral' }}>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} justifyContent="space-between" alignItems={{ md: 'center' }}>
                  <Box>
                    <Typography variant="subtitle1">{activeSection.label}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {activeView === 'human'
                        ? 'Usa esta cola para aprobar o rechazar acciones auditables que si requieren criterio humano.'
                        : 'Usa esta cola para resolver fallos del motor, parsing o checkpoints internos sin mezclarlo con aprobaciones de negocio.'}
                    </Typography>
                  </Box>
                  <Chip
                    size="small"
                    icon={<Iconify icon={activeSection.icon} width={14} />}
                    label={`${visibleItems.length} pendientes`}
                  />
                </Stack>
              </Card>
            </Stack>

            {!loading && visibleItems.length === 0 && (
              <Paper
                variant="outlined"
                sx={{
                  p: 6,
                  textAlign: 'center',
                  borderStyle: 'dashed',
                  bgcolor: alpha(
                    activeView === 'technical' ? theme.palette.info.main : theme.palette.success.main,
                    0.04
                  ),
                  borderColor: alpha(
                    activeView === 'technical' ? theme.palette.info.main : theme.palette.success.main,
                    0.3
                  ),
                }}
              >
                <Iconify
                  icon={activeView === 'technical' ? 'mdi:server-network' : 'mdi:check-decagram'}
                  width={64}
                  sx={{ color: activeView === 'technical' ? 'info.main' : 'success.main', mb: 2 }}
                />
                <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>
                  {activeView === 'technical' ? 'Sin checkpoints tecnicos pendientes' : 'Sin revisiones humanas pendientes'}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  {activeView === 'technical'
                    ? 'No hay excepciones tecnicas abiertas en este momento.'
                    : 'No hay acciones sensibles esperando aprobacion manual.'}
                </Typography>
              </Paper>
            )}

            {loading && visibleItems.length === 0 && (
              <Box sx={{ textAlign: 'center', py: 6 }}>
                <CircularProgress />
                <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                  Cargando cola de revision...
                </Typography>
              </Box>
            )}

            <Stack spacing={3}>
              {visibleItems.map((checkpoint) => (
                <SectionCard
                  key={checkpoint.checkpointId}
                  checkpoint={checkpoint}
                  deciding={decidingId === checkpoint.executionId}
                  onApprove={handleApprove}
                  onReject={(cp) => openDecisionDialog(cp, 'reject')}
                  onFallback={(cp) => openDecisionDialog(cp, 'fallback')}
                />
              ))}
            </Stack>
          </Card>
        </Stack>
      </DashboardContent>

      <Dialog open={!!decisionDialog} onClose={() => setDecisionDialog(null)} maxWidth="sm" fullWidth>
        <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify
            icon={decisionDialog?.action === 'fallback' ? 'mdi:transit-connection-variant' : 'mdi:alert'}
            sx={{ color: decisionDialog?.action === 'fallback' ? 'warning.main' : 'error.main' }}
          />
          {decisionDialog?.action === 'fallback' ? 'Enviar a fallback' : 'Cancelar ejecucion'}
        </DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {decisionDialog?.action === 'fallback'
              ? 'Opcionalmente agrega una nota para dejar trazabilidad del motivo del desvio tecnico.'
              : 'Indica por que la ejecucion no debe continuar. La ejecucion quedara como fallida.'}
          </Typography>
          <TextField
            autoFocus
            fullWidth
            multiline
            rows={3}
            label={decisionDialog?.action === 'fallback' ? 'Nota de fallback' : 'Motivo de cancelacion'}
            placeholder={
              decisionDialog?.action === 'fallback'
                ? 'Explica por que se envia a un flujo alterno...'
                : 'Explica por que esta accion no debe continuar...'
            }
            value={decisionFeedback}
            onChange={(e) => setDecisionFeedback(e.target.value)}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setDecisionDialog(null)}>Cancelar</Button>
          <Button
            variant="contained"
            color={decisionDialog?.action === 'fallback' ? 'warning' : 'error'}
            onClick={confirmDecision}
          >
            {decisionDialog?.action === 'fallback' ? 'Confirmar fallback' : 'Confirmar cancelacion'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
