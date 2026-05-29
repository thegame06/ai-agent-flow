import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Alert from '@mui/material/Alert';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
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
  const theme = useTheme();
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
  const readyConnections = orchestratorStatus?.connections?.filter((item) => item.ready).length ?? 0;
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
  const readinessTone = readinessPercent === 100 ? 'success' : readinessPercent >= 50 ? 'warning' : 'error';
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

        <Paper
          variant="outlined"
          sx={{
            p: { xs: 3, md: 4 },
            borderRadius: 4,
            overflow: 'hidden',
            borderColor:
              theme.palette.mode === 'dark'
                ? alpha(theme.palette.primary.light, 0.22)
                : alpha(theme.palette.primary.main, 0.16),
            background:
              theme.palette.mode === 'dark'
                ? `radial-gradient(circle at 6% 18%, ${alpha(theme.palette.primary.main, 0.2)}, transparent 30%), radial-gradient(circle at 94% 0%, ${alpha(
                    theme.palette.secondary.main,
                    0.16
                  )}, transparent 28%), linear-gradient(135deg, ${alpha(theme.palette.background.paper, 0.96)} 0%, ${alpha(
                    theme.palette.grey[900],
                    0.9
                  )} 100%)`
                : 'radial-gradient(circle at 6% 18%, rgba(14,124,90,0.18), transparent 28%), radial-gradient(circle at 94% 0%, rgba(0,167,181,0.18), transparent 26%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ xs: 'flex-start', md: 'center' }} sx={{ mb: 2.5 }}>
            <Avatar
              src="/logo/logo-single.svg"
              alt="AnnonAI"
              sx={{ width: 52, height: 52, bgcolor: 'transparent', boxShadow: `0 12px 32px ${alpha(theme.palette.primary.main, 0.22)}` }}
            />
            <Box sx={{ flex: 1 }}>
              <Typography variant="h4" sx={{ fontWeight: 800, letterSpacing: -0.6, lineHeight: 1.2 }}>
                Centro operativo
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Revisa qué falta para operar, crea automatizaciones y corrige bloqueos sin entrar a configuraciones técnicas.
              </Typography>
            </Box>
            <Chip
              size="small"
              icon={<Iconify icon={readinessPercent === 100 ? 'mdi:check-circle' : 'mdi:clock-outline'} width={14} />}
              label={readinessPercent === 100 ? 'Listo para operar' : 'Preparacion pendiente'}
              color={readinessPercent === 100 ? 'success' : 'warning'}
              variant="soft"
            />
          </Stack>

          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2.5} alignItems={{ xs: 'stretch', md: 'center' }} sx={{ mb: 3 }}>
            <Box sx={{ minWidth: { md: 220 } }}>
              <Typography variant="overline" color="text.secondary">
                Preparacion general
              </Typography>
              <Typography variant="h3" sx={{ lineHeight: 1.1 }}>
                {readinessPercent}%
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {readyCount} de {readinessItems.length} bloques listos
              </Typography>
            </Box>
            <Box sx={{ flex: 1 }}>
              <LinearProgress
                variant="determinate"
                value={readinessPercent}
                color={readinessTone}
                sx={{ height: 10, borderRadius: 999, mb: 1.25 }}
              />
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                {readinessItems.map((item) => (
                  <Chip
                    key={item.key}
                    size="small"
                    icon={<Iconify icon={item.icon} width={14} />}
                    label={`${item.label}: ${item.countLabel}`}
                    color={item.ready ? 'success' : 'default'}
                    variant={item.ready ? 'soft' : 'outlined'}
                    sx={{ bgcolor: item.ready ? undefined : alpha(theme.palette.background.paper, 0.72) }}
                  />
                ))}
              </Stack>
            </Box>
            <Stack spacing={1} sx={{ minWidth: { md: 240 } }}>
              <Button
                component={RouterLink}
                href={paths.dashboard.automationNew}
                variant="contained"
                startIcon={<Iconify icon="mdi:auto-fix" width={18} />}
                size="large"
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
          </Stack>

          {missingItems.length > 0 ? (
            <Alert severity="warning" sx={{ mb: 2.5 }}>
              <strong>Bloqueos actuales:</strong> {missingItems.map((item) => item.label).join(', ')}.
              {' '}Empieza por <strong>{nextStep.label.toLowerCase()}</strong>.
            </Alert>
          ) : (
            <Alert severity="success" sx={{ mb: 2.5 }}>
              La plataforma ya tiene los elementos mínimos para operar. El siguiente paso recomendado es publicar o mejorar una automatización.
            </Alert>
          )}

          <Divider sx={{ mb: 2.5 }} />

          <WizardLauncher value={wizardType} onChange={setWizardType} />
        </Paper>

        <GridSection title="Proximo paso" sx={{ mt: 3 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <ActionCard
              title={nextStep.ready ? 'Optimizar automatizaciones' : nextStep.actionLabel}
              description={
                nextStep.ready
                  ? 'Ya tienes la base operativa. Aprovecha para construir o mejorar una automatización con impacto directo.'
                  : `Aun falta completar ${nextStep.label.toLowerCase()} para operar con menos fricción.`
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
        </GridSection>

        <GridSection title="Accesos rapidos" sx={{ mt: 3 }}>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {quickLinks.map((link) => (
              <Button
                key={link.label}
                component={RouterLink}
                href={link.href}
                variant="outlined"
                size="small"
                startIcon={<Iconify icon={link.icon} width={16} />}
                sx={{ borderRadius: 6, color: 'text.secondary', borderColor: 'divider', '&:hover': { borderColor: 'primary.main', color: 'primary.main' } }}
              >
                {link.label}
              </Button>
            ))}
          </Stack>
        </GridSection>
      </DashboardContent>
    </>
  );
}

function GridSection({
  title,
  children,
  sx,
}: {
  title: string;
  children: React.ReactNode;
  sx?: object;
}) {
  return (
    <Box sx={sx}>
      <Typography variant="h6" sx={{ mb: 1.5 }}>
        {title}
      </Typography>
      {children}
    </Box>
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
    <Card variant="outlined" sx={{ flex: 1 }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Iconify icon={icon} width={20} />
            <Typography variant="subtitle1">{title}</Typography>
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
