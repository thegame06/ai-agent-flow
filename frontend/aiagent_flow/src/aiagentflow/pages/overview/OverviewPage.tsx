import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
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
  channels?: unknown[];
  connections?: Array<{ ready: boolean }>;
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
      .then((res) => { if (active) setOrchestratorStatus(res.data as SystemOrchestratorStatus); })
      .catch(() => { if (active) setOrchestratorStatus(null); });
    return () => {
      active = false;
    };
  }, [tenantId]);

  const workflowCount = orchestratorStatus?.workflows?.length ?? 0;
  const channelCount = orchestratorStatus?.channels?.length ?? 0;
  const readyConnections = orchestratorStatus?.connections?.filter((item) => item.ready).length ?? 0;
  const isReady = workflowCount > 0 && channelCount > 0;

  const quickLinks = [
    { label: 'Crear automatizacion', icon: 'mdi:auto-fix', href: paths.dashboard.automationNew },
    { label: 'Flujos automatizados', icon: 'mdi:source-branch', href: paths.dashboard.workflows },
    { label: 'Canales', icon: 'mdi:message-processing-outline', href: paths.dashboard.system.channels },
    { label: 'Integraciones', icon: 'mdi:connection', href: paths.dashboard.marketplace },
    { label: 'Asistentes IA', icon: 'mdi:robot-outline', href: paths.dashboard.agents },
  ];

  return (
    <>
      <Helmet>
        <title>Inicio | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
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
          <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2.5 }}>
            <Avatar
              src="/logo/logo-single.svg"
              alt="Annonai"
              sx={{ width: 52, height: 52, bgcolor: 'transparent', boxShadow: `0 12px 32px ${alpha(theme.palette.primary.main, 0.22)}` }}
            />
            <Box sx={{ flex: 1 }}>
              <Typography variant="h4" sx={{ fontWeight: 800, letterSpacing: -0.6, lineHeight: 1.2 }}>
                Inicio guiado
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Crea automatizaciones en modo chat, paso a paso, sin configuración técnica.
              </Typography>
            </Box>
            <Chip
              size="small"
              icon={<Iconify icon={isReady ? 'mdi:check-circle' : 'mdi:clock-outline'} width={14} />}
              label={isReady ? 'Listo para operar' : 'Configuración pendiente'}
              color={isReady ? 'success' : 'warning'}
              variant="soft"
            />
          </Stack>

          <Stack direction="row" spacing={1} sx={{ mb: 2.5 }} flexWrap="wrap">
            {[
              { label: `${workflowCount} flujo${workflowCount !== 1 ? 's' : ''}`, icon: 'mdi:source-branch' },
              { label: `${channelCount} canal${channelCount !== 1 ? 'es' : ''}`, icon: 'mdi:chat-processing-outline' },
              { label: `${readyConnections} integración${readyConnections !== 1 ? 'es' : ''}`, icon: 'mdi:connection' },
              { label: `${metrics.publishedAgents} asistente${metrics.publishedAgents !== 1 ? 's' : ''} activos`, icon: 'mdi:robot-outline' },
            ].map((item) => (
              <Chip
                key={item.label}
                size="small"
                icon={<Iconify icon={item.icon} width={14} />}
                label={item.label}
                variant="outlined"
                sx={{ bgcolor: alpha(theme.palette.background.paper, 0.7) }}
              />
            ))}
          </Stack>

          <Divider sx={{ mb: 2.5 }} />

          <WizardLauncher value={wizardType} onChange={setWizardType} />
        </Paper>

        <Stack direction="row" spacing={1} sx={{ mt: 2.5 }} flexWrap="wrap">
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
          <Box sx={{ flex: 1 }} />
          <Chip
            size="small"
            label={`${metrics.completedToday} ejecuciones hoy · ${Math.round(metrics.avgQualityScore * 100)}% calidad`}
            variant="outlined"
            sx={{ color: 'text.secondary' }}
          />
        </Stack>
      </DashboardContent>
    </>
  );
}
