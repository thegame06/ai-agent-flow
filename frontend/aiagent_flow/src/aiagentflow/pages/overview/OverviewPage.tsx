import type { RootState, AppDispatch } from 'src/aiagentflow/store';

import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';
import { useDispatch, useSelector } from 'react-redux';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
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

import { Iconify } from 'src/components/iconify';

import { fetchOverview } from './overviewSlice';

type SystemOrchestratorStatus = {
  gaps?: string[];
  workflows?: unknown[];
  channels?: unknown[];
  connections?: Array<{ ready: boolean }>;
};

type QuickAction = {
  title: string;
  helper: string;
  icon: string;
  href: string;
  cta: string;
};

function QuickActionCard({ title, helper, icon, href, cta }: QuickAction) {
  return (
    <Card variant="outlined" sx={{ height: '100%', borderRadius: 3 }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Avatar sx={{ width: 44, height: 44, bgcolor: 'primary.lighter', color: 'primary.main' }}>
            <Iconify icon={icon} width={23} />
          </Avatar>
          <Box>
            <Typography variant="h6">{title}</Typography>
            <Typography variant="body2" color="text.secondary">
              {helper}
            </Typography>
          </Box>
          <Button component={RouterLink} href={href} variant="outlined" size="small" sx={{ alignSelf: 'flex-start' }}>
            {cta}
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

export default function OverviewPage() {
  const dispatch = useDispatch<AppDispatch>();
  const tenantId = useTenantId();
  const theme = useTheme();
  const [assistantPrompt, setAssistantPrompt] = useState('');
  const [orchestratorStatus, setOrchestratorStatus] = useState<SystemOrchestratorStatus | null>(null);
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
  const readyConnections = orchestratorStatus?.connections?.filter((connection) => connection.ready).length ?? 0;
  const systemHint =
    orchestratorStatus?.gaps?.[0] ??
    'Todo listo para construir: crea una intencion, selecciona un agente y conecta la accion que quieres automatizar.';

  const quickActions: QuickAction[] = [
    {
      title: 'Crear un workflow',
      helper: 'Diseña el comportamiento del negocio con nodos simples e intenciones claras.',
      icon: 'mdi:source-branch',
      href: paths.dashboard.workflows,
      cta: 'Abrir studio',
    },
    {
      title: 'Conectar un canal',
      helper: 'Activa WhatsApp, web chat, voz, call center o email para recibir eventos reales.',
      icon: 'mdi:message-processing-outline',
      href: paths.dashboard.system.channels,
      cta: 'Ver canales',
    },
    {
      title: 'Agregar una integracion',
      helper: 'Configura Twilio, APIs, Storage, Drive o MCP una vez y reutilizalos.',
      icon: 'mdi:connection',
      href: paths.dashboard.marketplace,
      cta: 'Ir a Marketplace',
    },
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
            mb: 3,
            p: { xs: 2.5, md: 4 },
            borderRadius: 4,
            overflow: 'hidden',
            position: 'relative',
            borderColor: alpha(theme.palette.primary.main, 0.16),
            background:
              'radial-gradient(circle at 6% 18%, rgba(14,124,90,0.20), transparent 30%), radial-gradient(circle at 96% 0%, rgba(0,167,181,0.20), transparent 28%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Grid container spacing={3} alignItems="center">
            <Grid item xs={12} md={8}>
              <Stack spacing={2.5}>
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Avatar
                    src="/logo/logo-single.svg"
                    alt="Annonai"
                    sx={{
                      width: 64,
                      height: 64,
                      bgcolor: 'transparent',
                      boxShadow: `0 18px 42px ${alpha(theme.palette.primary.main, 0.22)}`,
                    }}
                  />
                  <Box>
                    <Typography variant="overline" color="text.secondary">
                      Annonai
                    </Typography>
                    <Typography variant="h3" sx={{ fontWeight: 900, letterSpacing: -0.8 }}>
                      hola, soy annonai, tu amigo inteligente.
                    </Typography>
                  </Box>
                </Stack>

                <Typography variant="body1" color="text.secondary" sx={{ maxWidth: 760 }}>
                  Dime que quieres automatizar y te guio al lugar correcto: canal, agente,
                  integracion o workflow. La plataforma se encarga de traducirlo a eventos,
                  intenciones y nodos.
                </Typography>

                <Paper sx={{ p: 1, maxWidth: 760, borderRadius: 2.5, boxShadow: 8 }}>
                  <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                    <TextField
                      fullWidth
                      size="small"
                      value={assistantPrompt}
                      onChange={(event) => setAssistantPrompt(event.target.value)}
                      placeholder="Ejemplo: quiero atender WhatsApp y agendar citas automaticamente"
                    />
                    <Button
                      component={RouterLink}
                      href={paths.dashboard.workflows}
                      variant="contained"
                      startIcon={<Iconify icon="mdi:creation-outline" />}
                    >
                      Empezar
                    </Button>
                  </Stack>
                </Paper>

                <Alert severity="info" sx={{ maxWidth: 760, borderRadius: 2 }}>
                  {systemHint}
                </Alert>
              </Stack>
            </Grid>

            <Grid item xs={12} md={4}>
              <Card variant="outlined" sx={{ borderRadius: 3, bgcolor: 'rgba(255,255,255,0.78)' }}>
                <CardContent>
                  <Stack spacing={2}>
                    <Box>
                      <Typography variant="h6">Estado del espacio</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Solo lo esencial para saber si puedes operar.
                      </Typography>
                    </Box>
                    {[
                      ['Workflows', workflowCount, 'mdi:source-branch'],
                      ['Canales', channelCount, 'mdi:chat-processing-outline'],
                      ['Integraciones listas', readyConnections, 'mdi:connection'],
                    ].map(([label, value, icon]) => (
                      <Stack key={String(label)} direction="row" spacing={1.5} alignItems="center">
                        <Avatar sx={{ width: 36, height: 36, bgcolor: 'background.neutral', color: 'primary.main' }}>
                          <Iconify icon={String(icon)} width={20} />
                        </Avatar>
                        <Box sx={{ flexGrow: 1 }}>
                          <Typography variant="subtitle2">{label}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {Number(value) > 0 ? 'Configurado' : 'Pendiente'}
                          </Typography>
                        </Box>
                        <Typography variant="h5">{String(value)}</Typography>
                      </Stack>
                    ))}
                    <Chip
                      color={workflowCount > 0 && channelCount > 0 ? 'success' : 'warning'}
                      label={workflowCount > 0 && channelCount > 0 ? 'Listo para probar' : 'Configuracion pendiente'}
                      sx={{ alignSelf: 'flex-start' }}
                    />
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          </Grid>
        </Paper>

        <Grid container spacing={2.5} sx={{ mb: 3 }}>
          {quickActions.map((action) => (
            <Grid key={action.title} item xs={12} md={4}>
              <QuickActionCard {...action} />
            </Grid>
          ))}
        </Grid>

        <Card variant="outlined" sx={{ borderRadius: 3 }}>
          <CardContent>
            <Grid container spacing={2} alignItems="center">
              <Grid item xs={12} md={8}>
                <Typography variant="h6">Resumen operativo</Typography>
                <Typography variant="body2" color="text.secondary">
                  Este bloque queda como referencia rapida; la accion principal esta arriba.
                </Typography>
              </Grid>
              <Grid item xs={12} md={4}>
                <Stack direction="row" spacing={1} justifyContent={{ xs: 'flex-start', md: 'flex-end' }} flexWrap="wrap">
                  <Chip label={`${metrics.completedToday} ejecuciones hoy`} />
                  <Chip label={`${metrics.publishedAgents}/${metrics.totalAgents} agentes publicados`} />
                  <Chip label={`${Math.round(metrics.avgQualityScore * 100)}% calidad`} />
                </Stack>
              </Grid>
            </Grid>
          </CardContent>
        </Card>
      </DashboardContent>
    </>
  );
}
