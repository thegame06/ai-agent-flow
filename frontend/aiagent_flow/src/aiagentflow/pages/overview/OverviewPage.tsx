import type { ReactNode } from 'react';
import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';
import { type WizardId, WizardLauncher } from 'src/aiagentflow/components/chat-wizard/WizardRegistry';

import { Iconify } from 'src/components/iconify';

import { fetchOverview } from './overviewSlice';

type SystemOrchestratorStatus = {
  workflows?: unknown[];
  channels?: Array<{ status?: string }>;
  connections?: Array<{ ready: boolean }>;
};

type ReadinessItem = {
  key: string;
  label: string;
  ready: boolean;
  countLabel: string;
  href: string;
  actionLabel: string;
  icon: string;
};

export default function OverviewPage() {
  const dispatch = useDispatch<AppDispatch>();
  const tenantId = useTenantId();
  const [orchestratorStatus, setOrchestratorStatus] = useState<SystemOrchestratorStatus | null>(null);
  const [wizardType, setWizardType] = useState<WizardId>('automation');

  const { metrics, loading } = useSelector((state: RootState) => state.overview);

  useEffect(() => {
    dispatch(fetchOverview(tenantId));
  }, [dispatch, tenantId]);

  useEffect(() => {
    let active = true;
    axios
      .get(endpoints.agentflow.systemOrchestrator.status(tenantId))
      .then((res) => {
        if (active) setOrchestratorStatus(res.data as SystemOrchestratorStatus);
      })
      .catch(() => {
        if (active) setOrchestratorStatus(null);
      });

    return () => {
      active = false;
    };
  }, [tenantId]);

  const workflowCount = orchestratorStatus?.workflows?.length ?? 0;
  const channelCount = orchestratorStatus?.channels?.length ?? 0;
  const readyConnections =
    orchestratorStatus?.connections?.filter((item) => item.ready).length ?? 0;

  const readinessItems: ReadinessItem[] = useMemo(
    () => [
      {
        key: 'channels',
        label: 'Canales conectados',
        ready: channelCount > 0,
        countLabel: `${channelCount} canal${channelCount === 1 ? '' : 'es'}`,
        href: paths.dashboard.system.channels,
        actionLabel: channelCount > 0 ? 'Revisar canales' : 'Conectar canal',
        icon: 'mdi:access-point',
      },
      {
        key: 'integrations',
        label: 'Integraciones listas',
        ready: readyConnections > 0,
        countLabel: `${readyConnections} integracion${readyConnections === 1 ? '' : 'es'}`,
        href: paths.dashboard.marketplace,
        actionLabel: readyConnections > 0 ? 'Revisar integraciones' : 'Activar integracion',
        icon: 'mdi:connection',
      },
      {
        key: 'automation',
        label: 'Automatizaciones publicadas',
        ready: workflowCount > 0,
        countLabel: `${workflowCount} automatizacion${workflowCount === 1 ? '' : 'es'}`,
        href: paths.dashboard.workflows,
        actionLabel: workflowCount > 0 ? 'Ver automatizaciones' : 'Crear automatizacion',
        icon: 'mdi:source-branch',
      },
      {
        key: 'agents',
        label: 'Asistentes activos',
        ready: metrics.publishedAgents > 0,
        countLabel: `${metrics.publishedAgents} asistente${metrics.publishedAgents === 1 ? '' : 's'}`,
        href: paths.dashboard.agents,
        actionLabel: metrics.publishedAgents > 0 ? 'Ver asistentes' : 'Crear asistente',
        icon: 'mdi:robot-outline',
      },
    ],
    [channelCount, metrics.publishedAgents, readyConnections, workflowCount]
  );

  const readyCount = readinessItems.filter((item) => item.ready).length;
  const readinessPercent = Math.round((readyCount / readinessItems.length) * 100);
  const readinessTone =
    readinessPercent === 100 ? 'success' : readinessPercent >= 50 ? 'warning' : 'error';
  const missingItems = readinessItems.filter((item) => !item.ready);
  const nextStep = missingItems[0] ?? readinessItems[0];

  const quickLinks = [
    { label: 'Crear automatizacion', icon: 'mdi:auto-fix', href: paths.dashboard.automationNew },
    { label: 'Canales', icon: 'mdi:message-processing-outline', href: paths.dashboard.system.channels },
    { label: 'Integraciones', icon: 'mdi:connection', href: paths.dashboard.marketplace },
    { label: 'Asistentes', icon: 'mdi:robot-outline', href: paths.dashboard.agents },
    { label: 'Actividad', icon: 'mdi:chart-timeline-variant', href: paths.dashboard.executions },
  ];

  return (
    <>
      <Helmet>
        <title>Inicio | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        {loading && <LinearProgress sx={{ mb: 2 }} />}

        <BrandPageHeader
          eyebrow="Centro operativo"
          title="Preparacion de la plataforma"
          description="Revisa lo que falta para operar, publica automatizaciones y corrige bloqueos sin entrar a configuraciones tecnicas."
          icon="mdi:view-dashboard-outline"
          meta={
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              <Chip
                size="small"
                color={readinessPercent === 100 ? 'success' : 'warning'}
                label={readinessPercent === 100 ? 'Listo para operar' : 'Preparacion pendiente'}
              />
              <Chip
                size="small"
                variant="outlined"
                color="info"
                label={`${readyCount} de ${readinessItems.length} bloques listos`}
              />
            </Stack>
          }
          actions={
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button
                component={RouterLink}
                href={paths.dashboard.automationNew}
                variant="contained"
                startIcon={<Iconify icon="mdi:auto-fix" width={18} />}
              >
                Crear automatizacion
              </Button>
              <Button
                component={RouterLink}
                href={nextStep.href}
                variant="outlined"
                startIcon={<Iconify icon={nextStep.icon} width={18} />}
              >
                {nextStep.actionLabel}
              </Button>
            </Stack>
          }
        />

        <Grid container spacing={2.5}>
          <Grid item xs={12} lg={8}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, height: '100%' }}>
              <Stack spacing={2}>
                <Box>
                  <Typography variant="overline" color="text.secondary">
                    Preparacion general
                  </Typography>
                  <Stack
                    direction={{ xs: 'column', sm: 'row' }}
                    spacing={1.5}
                    justifyContent="space-between"
                    alignItems={{ sm: 'flex-end' }}
                    sx={{ mt: 0.5 }}
                  >
                    <Box>
                      <Typography variant="h3" sx={{ lineHeight: 1 }}>
                        {readinessPercent}%
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        {readyCount} de {readinessItems.length} bloques listos
                      </Typography>
                    </Box>
                    <Chip
                      size="small"
                      color={readinessTone}
                      label={nextStep.ready ? 'Base operativa lista' : `Siguiente foco: ${nextStep.label}`}
                    />
                  </Stack>
                </Box>

                <LinearProgress
                  variant="determinate"
                  value={readinessPercent}
                  color={readinessTone}
                  sx={{ height: 10, borderRadius: 999 }}
                />

                <Grid container spacing={1.5}>
                  {readinessItems.map((item) => (
                    <Grid key={item.key} item xs={12} sm={6}>
                      <Card
                        variant="outlined"
                        sx={{
                          p: 1.75,
                          borderRadius: 2.5,
                          borderColor: item.ready ? 'success.light' : 'divider',
                          bgcolor: item.ready ? 'success.lighter' : 'background.paper',
                        }}
                      >
                        <Stack spacing={1.25}>
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Iconify icon={item.icon} width={18} />
                            <Typography variant="subtitle2">{item.label}</Typography>
                          </Stack>
                          <Typography variant="body2" color="text.secondary">
                            {item.countLabel}
                          </Typography>
                          <Box>
                            <Button
                              component={RouterLink}
                              href={item.href}
                              size="small"
                              variant={item.ready ? 'text' : 'outlined'}
                            >
                              {item.actionLabel}
                            </Button>
                          </Box>
                        </Stack>
                      </Card>
                    </Grid>
                  ))}
                </Grid>

                {missingItems.length > 0 ? (
                  <Alert severity="warning">
                    <strong>Bloqueos actuales:</strong> {missingItems.map((item) => item.label).join(', ')}.
                    {' '}Empieza por <strong>{nextStep.label.toLowerCase()}</strong>.
                  </Alert>
                ) : (
                  <Alert severity="success">
                    La base operativa ya esta lista. El siguiente paso recomendado es publicar o mejorar una automatizacion.
                  </Alert>
                )}
              </Stack>
            </Card>
          </Grid>

          <Grid item xs={12} lg={4}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, height: '100%' }}>
              <Stack spacing={2}>
                <Box>
                  <Typography variant="subtitle1">Acciones recomendadas</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Mantiene el ritmo operativo sin perderte en menus secundarios.
                  </Typography>
                </Box>
                <ActionCard
                  title={nextStep.ready ? 'Optimizar automatizaciones' : nextStep.actionLabel}
                  description={
                    nextStep.ready
                      ? 'Ya tienes la base lista. Aprovecha para construir o mejorar una automatizacion con impacto directo.'
                      : `Aun falta completar ${nextStep.label.toLowerCase()} para operar con menos friccion.`
                  }
                  href={nextStep.ready ? paths.dashboard.automationNew : nextStep.href}
                  cta={nextStep.ready ? 'Abrir creador' : nextStep.actionLabel}
                  icon={nextStep.ready ? 'mdi:auto-fix' : nextStep.icon}
                />
                <ActionCard
                  title="Monitorear actividad"
                  description={`Hoy llevas ${metrics.completedToday} ejecuciones completadas y ${Math.round(metrics.avgQualityScore * 100)}% de calidad promedio.`}
                  href={paths.dashboard.executions}
                  cta="Ver actividad"
                  icon="mdi:chart-timeline-variant"
                />
              </Stack>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3 }}>
              <Stack spacing={2}>
                <Box>
                  <Typography variant="subtitle1">Accesos rapidos</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Atajos para las tareas que mas se repiten en operacion.
                  </Typography>
                </Box>
                <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                  {quickLinks.map((link) => (
                    <Button
                      key={link.label}
                      component={RouterLink}
                      href={link.href}
                      variant="outlined"
                      size="small"
                      startIcon={<Iconify icon={link.icon} width={16} />}
                      sx={{ borderRadius: 6 }}
                    >
                      {link.label}
                    </Button>
                  ))}
                </Stack>
              </Stack>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3 }}>
              <Stack spacing={2}>
                <Box>
                  <Typography variant="subtitle1">Asistente guiado</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Usa una guia conversacional para preparar la siguiente automatizacion.
                  </Typography>
                </Box>
                <WizardLauncher value={wizardType} onChange={setWizardType} />
              </Stack>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}

function ActionCard({
  title,
  description,
  href,
  cta,
  icon,
}: {
  title: string;
  description: string;
  href: string;
  cta: string;
  icon: string;
}) {
  return (
    <Card variant="outlined" sx={{ borderRadius: 2.5 }}>
      <CardContent>
        <Stack spacing={1.25}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Iconify icon={icon} width={20} />
            <Typography variant="subtitle2">{title}</Typography>
          </Stack>
          <Typography variant="body2" color="text.secondary">
            {description}
          </Typography>
          <Box>
            <Button component={RouterLink} href={href} variant="outlined" size="small">
              {cta}
            </Button>
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
}
