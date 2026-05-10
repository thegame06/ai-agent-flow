import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useEffect } from 'react';
import { Helmet } from 'react-helmet-async';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { fetchOverview } from './overviewSlice';

// ─── Stat Card ───────────────────────────────────────────────────────────
interface StatCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: string;
  gradient: string;
  trend?: { value: number; label: string };
}

function StatCard({ title, value, subtitle, icon, gradient, trend }: StatCardProps) {
  const theme = useTheme();
  return (
    <Card
      sx={{
        position: 'relative',
        overflow: 'hidden',
        transition: 'transform 0.2s, box-shadow 0.2s',
        '&:hover': { transform: 'translateY(-2px)', boxShadow: theme.shadows[12] },
      }}
    >
      <CardContent sx={{ p: 3 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
          <Box>
            <Typography variant="overline" sx={{ color: 'text.secondary', letterSpacing: 1.2, fontSize: '0.68rem' }}>
              {title}
            </Typography>
            <Typography variant="h3" sx={{ mt: 0.5, fontWeight: 800 }}>
              {value}
            </Typography>
            {subtitle && (
              <Typography variant="caption" sx={{ color: 'text.secondary', mt: 0.5 }}>
                {subtitle}
              </Typography>
            )}
            {trend && (
              <Stack direction="row" alignItems="center" spacing={0.5} sx={{ mt: 1 }}>
                <Iconify
                  icon={trend.value >= 0 ? 'mdi:trending-up' : 'mdi:trending-down'}
                  width={18}
                  sx={{ color: trend.value >= 0 ? 'success.main' : 'error.main' }}
                />
                <Typography variant="caption" sx={{ fontWeight: 700, color: trend.value >= 0 ? 'success.main' : 'error.main' }}>
                  {trend.value > 0 ? '+' : ''}{trend.value}%
                </Typography>
                <Typography variant="caption" sx={{ color: 'text.disabled' }}>
                  {trend.label}
                </Typography>
              </Stack>
            )}
          </Box>
          <Avatar
            sx={{
              width: 56,
              height: 56,
              background: gradient,
              boxShadow: `0 4px 14px 0 ${alpha(theme.palette.primary.main, 0.24)}`,
            }}
          >
            <Iconify icon={icon} width={28} sx={{ color: '#fff' }} />
          </Avatar>
        </Stack>
      </CardContent>
    </Card>
  );
}

// ─── Status Chip ─────────────────────────────────────────────────────────
function StatusChip({ status }: { status: string }) {
  const labels: Record<string, string> = {
    Completed: 'Completada',
    Running: 'En ejecucion',
    Failed: 'Fallida',
    HumanReviewPending: 'Revision humana',
    Published: 'Publicado',
    Draft: 'Borrador',
  };
  const colors: Record<string, 'success' | 'warning' | 'error' | 'info' | 'default'> = {
    Completed: 'success',
    Running: 'info',
    Failed: 'error',
    HumanReviewPending: 'warning',
    Published: 'success',
    Draft: 'default',
  };
  return <Chip label={labels[status] ?? status} size="small" color={colors[status] || 'default'} variant="soft" />;
}

// ─── Quality Bar ─────────────────────────────────────────────────────────
function QualityBar({ score }: { score: number }) {
  const theme = useTheme();
  const percent = Math.round(score * 100);
  const barColor = percent >= 80 ? theme.palette.success.main : percent >= 60 ? theme.palette.warning.main : theme.palette.error.main;

  return (
    <Stack direction="row" alignItems="center" spacing={1.5} sx={{ minWidth: 120 }}>
      <LinearProgress
        variant="determinate"
        value={percent}
        sx={{
          flex: 1,
          height: 8,
          borderRadius: 4,
          bgcolor: alpha(barColor, 0.16),
          '& .MuiLinearProgress-bar': { borderRadius: 4, bgcolor: barColor },
        }}
      />
      <Typography variant="caption" fontWeight={700}>
        {percent}%
      </Typography>
    </Stack>
  );
}

// ══════════════════════════════════════════════════════════════════════════
// OVERVIEW PAGE
// ══════════════════════════════════════════════════════════════════════════

export default function OverviewPage() {
  const dispatch = useDispatch<AppDispatch>();
  const tenantId = useTenantId();
  const theme = useTheme();
  const { metrics, recentExecutions, agentPerformance, loading } = useSelector(
    (state: RootState) => state.overview
  );

  useEffect(() => {
    dispatch(fetchOverview(tenantId));
  }, [dispatch, tenantId]);

  return (
    <>
      <Helmet>
        <title>Overview | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        {/* ── Header ── */}
        <Paper
          variant="outlined"
          sx={{
            mb: 4,
            p: { xs: 3, md: 5 },
            borderRadius: 3,
            background:
              'radial-gradient(circle at top left, rgba(38,103,255,0.12), transparent 34%), linear-gradient(135deg, #fbfcff 0%, #f4f7fb 100%)',
          }}
        >
          <Stack spacing={3} alignItems="center" textAlign="center">
            <Box>
              <Typography variant="overline" color="text.secondary">
                Inicio
              </Typography>
              <Typography variant="h3" sx={{ fontWeight: 800 }}>
                Jelou, Bladimir
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Construye flujos, conecta canales y revisa la operacion desde un solo lugar.
              </Typography>
            </Box>
            <Paper sx={{ p: 1.5, width: 1, maxWidth: 760, borderRadius: 2, boxShadow: 6 }}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                <TextField fullWidth placeholder="Que quieres construir o revisar?" size="small" />
                <Button
                  component={RouterLink}
                  href={paths.dashboard.workflows}
                  variant="contained"
                  startIcon={<Iconify icon="mdi:hammer-wrench" />}
                >
                  Construir
                </Button>
              </Stack>
            </Paper>
            <Stack direction="row" spacing={1} flexWrap="wrap" justifyContent="center">
              {[
                ['Preguntando', paths.dashboard.threads],
                ['Planificando', paths.dashboard.overview],
                ['Construyendo', paths.dashboard.workflows],
                ['Insights', paths.dashboard.executions],
              ].map(([label, href]) => (
                <Button key={label} component={RouterLink} href={href} variant="soft" size="small">
                  {label}
                </Button>
              ))}
            </Stack>
            <Stack direction="row" spacing={1} flexWrap="wrap" justifyContent="center">
              {[
                ['Workflow Studio', paths.dashboard.workflows],
                ['Marketplace', paths.dashboard.marketplace],
                ['Bandeja', paths.dashboard.threads],
                ['Canales', paths.dashboard.system.channels],
              ].map(([label, href]) => (
                <Button key={label} component={RouterLink} href={href} variant="outlined" size="small">
                  {label}
                </Button>
              ))}
            </Stack>
          </Stack>
        </Paper>

        {/* ── Metrics Grid ── */}
        <Grid container spacing={3} sx={{ mb: 4 }}>
          <Grid item xs={12} sm={6} md={3}>
            <StatCard
              title="Agentes"
              value={metrics.totalAgents}
              subtitle={`${metrics.publishedAgents} publicados � ${metrics.draftAgents} borradores`}
              icon="mdi:robot-outline"
              gradient={`linear-gradient(135deg, ${theme.palette.primary.main} 0%, ${theme.palette.primary.dark} 100%)`}
              trend={{ value: 12, label: 'vs semana anterior' }}
            />
          </Grid>
          <Grid item xs={12} sm={6} md={3}>
            <StatCard
              title="Ejecuciones de hoy"
              value={metrics.completedToday}
              subtitle={`${metrics.runningExecutions} en ejecucion � ${metrics.failedToday} fallidas`}
              icon="mdi:play-circle-outline"
              gradient={`linear-gradient(135deg, ${theme.palette.info.main} 0%, ${theme.palette.info.dark} 100%)`}
              trend={{ value: 23, label: 'vs ayer' }}
            />
          </Grid>
          <Grid item xs={12} sm={6} md={3}>
            <StatCard
              title="Revision pendiente"
              value={metrics.pendingCheckpoints}
              subtitle="Cola de aprobacion humana"
              icon="mdi:account-check-outline"
              gradient={`linear-gradient(135deg, ${theme.palette.warning.main} 0%, ${theme.palette.warning.dark} 100%)`}
            />
          </Grid>
          <Grid item xs={12} sm={6} md={3}>
            <StatCard
              title="Calidad"
              value={`${Math.round(metrics.avgQualityScore * 100)}%`}
              subtitle={`Latencia promedio: ${metrics.avgLatencyMs}ms`}
              icon="mdi:chart-line"
              gradient={`linear-gradient(135deg, ${theme.palette.success.main} 0%, ${theme.palette.success.dark} 100%)`}
              trend={{ value: 5, label: 'mejora' }}
            />
          </Grid>
        </Grid>

        {/* ── Two-Column Section ── */}
        <Grid container spacing={3}>
          {/* Ejecuciones recientes */}
          <Grid item xs={12} lg={7}>
            <Card sx={{ height: '100%' }}>
              <CardContent>
                <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
                  <Box>
                    <Typography variant="h6" fontWeight={700}>Ejecuciones recientes</Typography>
                    <Typography variant="caption" color="text.secondary">Ultimas 10 corridas de agentes</Typography>
                  </Box>
                  <Button
                    component={RouterLink}
                    href={paths.dashboard.executions}
                    size="small"
                    endIcon={<Iconify icon="mdi:arrow-right" />}
                  >
                    Ver todo
                  </Button>
                </Stack>

                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Agente</TableCell>
                      <TableCell>Estado</TableCell>
                      <TableCell align="right">Pasos</TableCell>
                      <TableCell align="right">Duracion</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {loading && (
                      <TableRow>
                        <TableCell colSpan={4} sx={{ textAlign: 'center', py: 4 }}>
                          <Typography variant="body2" color="text.secondary">Cargando...</Typography>
                        </TableCell>
                      </TableRow>
                    )}
                    {!loading && recentExecutions.length === 0 && (
                      <TableRow>
                        <TableCell colSpan={4} sx={{ textAlign: 'center', py: 4 }}>
                          <Stack alignItems="center" spacing={1}>
                            <Iconify icon="mdi:robot-off-outline" width={40} sx={{ color: 'text.disabled' }} />
                            <Typography variant="body2" color="text.secondary">
                              Aun no hay ejecuciones. Ejecuta tu primer agente.
                            </Typography>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    )}
                    {recentExecutions.map((exec) => (
                      <TableRow
                        key={exec.id}
                        hover
                        sx={{ cursor: 'pointer', '&:last-child td': { borderBottom: 0 } }}
                      >
                        <TableCell>
                          <Stack direction="row" alignItems="center" spacing={1.5}>
                            <Avatar
                              sx={{
                                width: 32,
                                height: 32,
                                bgcolor: alpha(theme.palette.primary.main, 0.08),
                                color: 'primary.main',
                                fontSize: '0.8rem',
                                fontWeight: 700,
                              }}
                            >
                              {exec.agentName.charAt(0).toUpperCase()}
                            </Avatar>
                            <Box>
                              <Typography variant="subtitle2" noWrap sx={{ maxWidth: 180 }}>
                                {exec.agentName}
                              </Typography>
                              <Typography variant="caption" color="text.disabled">
                                {exec.id.slice(0, 8)}…
                              </Typography>
                            </Box>
                          </Stack>
                        </TableCell>
                        <TableCell>
                          <StatusChip status={exec.status} />
                        </TableCell>
                        <TableCell align="right">
                          <Typography variant="body2" fontWeight={600}>{exec.totalSteps}</Typography>
                        </TableCell>
                        <TableCell align="right">
                          <Typography variant="body2">{exec.durationMs}ms</Typography>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </Grid>

          {/* Rendimiento de agentes */}
          <Grid item xs={12} lg={5}>
            <Card sx={{ height: '100%' }}>
              <CardContent>
                <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
                  <Box>
                    <Typography variant="h6" fontWeight={700}>Rendimiento de agentes</Typography>
                    <Typography variant="caption" color="text.secondary">Metricas de calidad y confiabilidad</Typography>
                  </Box>
                  <Button
                    component={RouterLink}
                    href={paths.dashboard.agents}
                    size="small"
                    endIcon={<Iconify icon="mdi:arrow-right" />}
                  >
                    Gestionar
                  </Button>
                </Stack>

                <Stack spacing={2}>
                  {agentPerformance.length === 0 && !loading && (
                    <Paper variant="outlined" sx={{ p: 3, textAlign: 'center' }}>
                      <Iconify icon="mdi:chart-box-outline" width={40} sx={{ color: 'text.disabled', mb: 1 }} />
                      <Typography variant="body2" color="text.secondary">
                        Aun no hay datos de agentes.
                      </Typography>
                    </Paper>
                  )}
                  {agentPerformance.slice(0, 5).map((agent) => (
                    <Paper
                      key={agent.agentKey}
                      variant="outlined"
                      sx={{
                        p: 2,
                        transition: 'all 0.2s',
                        '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.04), borderColor: 'primary.light' },
                      }}
                    >
                      <Stack direction="row" justifyContent="space-between" alignItems="center">
                        <Box sx={{ flex: 1, minWidth: 0 }}>
                          <Stack direction="row" alignItems="center" spacing={1}>
                            <Typography variant="subtitle2" noWrap>{agent.agentName}</Typography>
                            <StatusChip status={agent.status} />
                          </Stack>
                          <Typography variant="caption" color="text.secondary">
                            {agent.executionCount} ejecuciones � {Math.round(agent.avgDurationMs)}ms promedio � {(agent.failureRate * 100).toFixed(1)}% fallas
                          </Typography>
                        </Box>
                        <Box sx={{ minWidth: 130 }}>
                          <QualityBar score={agent.avgQualityScore} />
                        </Box>
                      </Stack>
                    </Paper>
                  ))}
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        </Grid>

        {/* ── Acciones rapidas ── */}
        <Paper
          variant="outlined"
          sx={{
            mt: 4,
            p: 3,
            background: `linear-gradient(135deg, ${alpha(theme.palette.primary.main, 0.04)} 0%, ${alpha(theme.palette.secondary.main, 0.04)} 100%)`,
            borderStyle: 'dashed',
          }}
        >
          <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems="center" spacing={2}>
            <Box>
              <Typography variant="h6" fontWeight={700}>Acciones rapidas</Typography>
              <Typography variant="body2" color="text.secondary">
                Atajos para construir, revisar y auditar la operacion
              </Typography>
            </Box>
            <Stack direction="row" spacing={1.5} flexWrap="wrap">
              <Button
                component={RouterLink}
                href={paths.dashboard.agentDesigner}
                variant="contained"
                startIcon={<Iconify icon="mdi:plus" />}
              >
                Nuevo agente
              </Button>
              <Button
                component={RouterLink}
                href={paths.dashboard.checkpoints}
                variant="outlined"
                startIcon={<Iconify icon="mdi:account-check" />}
              >
                Revision humana
              </Button>
              <Button
                component={RouterLink}
                href={paths.dashboard.governance.audit}
                variant="outlined"
                startIcon={<Iconify icon="mdi:shield-check" />}
              >
                Auditoria
              </Button>
            </Stack>
          </Stack>
        </Paper>
      </DashboardContent>
    </>
  );
}
