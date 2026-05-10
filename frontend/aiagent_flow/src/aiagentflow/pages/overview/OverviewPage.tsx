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
  events?: unknown[];
};

type ActionCardProps = {
  title: string;
  helper: string;
  icon: string;
  href: string;
  cta: string;
  color?: 'primary' | 'success' | 'warning' | 'info';
};

function ActionCard({ title, helper, icon, href, cta, color = 'primary' }: ActionCardProps) {
  return (
    <Card variant="outlined" sx={{ height: '100%', borderRadius: 3 }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Avatar
            sx={{
              width: 46,
              height: 46,
              bgcolor: `${color}.lighter`,
              color: `${color}.main`,
            }}
          >
            <Iconify icon={icon} width={24} />
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

  const readyConnections = orchestratorStatus?.connections?.filter((connection) => connection.ready).length ?? 0;
  const systemHint =
    orchestratorStatus?.gaps?.[0] ??
    'Empieza conectando un canal, selecciona un agente y crea un workflow con una intencion clara.';

  const readinessItems = [
    {
      label: 'Workflows',
      value: orchestratorStatus?.workflows?.length ?? 0,
      icon: 'mdi:source-branch',
      helper: 'Flujos de negocio creados',
    },
    {
      label: 'Canales',
      value: orchestratorStatus?.channels?.length ?? 0,
      icon: 'mdi:chat-processing-outline',
      helper: 'Entradas conectadas',
    },
    {
      label: 'Integraciones',
      value: readyConnections,
      icon: 'mdi:connection',
      helper: 'Listas para usar',
    },
    {
      label: 'Eventos',
      value: orchestratorStatus?.events?.length ?? 0,
      icon: 'mdi:flash-outline',
      helper: 'Disparadores del sistema',
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
            background:
              'radial-gradient(circle at 8% 18%, rgba(14,124,90,0.18), transparent 28%), radial-gradient(circle at 92% 6%, rgba(0,167,181,0.18), transparent 26%), linear-gradient(135deg, #fbfdf9 0%, #f3f9f5 100%)',
          }}
        >
          <Grid container spacing={3} alignItems="center">
            <Grid item xs={12} md={7}>
              <Stack spacing={2.2}>
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Avatar
                    src="/logo/logo-single.svg"
                    alt="Annonai"
                    sx={{
                      width: 62,
                      height: 62,
                      bgcolor: 'transparent',
                      boxShadow: `0 18px 40px ${alpha(theme.palette.primary.main, 0.22)}`,
                    }}
                  />
                  <Box>
                    <Typography variant="overline" color="text.secondary">
                      Tu asistente de plataforma
                    </Typography>
                    <Typography variant="h3" sx={{ fontWeight: 900, letterSpacing: -0.8 }}>
                      hola, soy annonai, tu amigo inteligente.
                    </Typography>
                  </Box>
                </Stack>

                <Typography variant="body1" color="text.secondary" sx={{ maxWidth: 720 }}>
                  Te ayudo a convertir canales, agentes e integraciones en workflows simples de entender.
                  No necesitas saber nombres tecnicos: dime que quieres automatizar y te llevo al lugar correcto.
                </Typography>

                <Paper sx={{ p: 1, maxWidth: 760, borderRadius: 2.5, boxShadow: 8 }}>
                  <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                    <TextField
                      fullWidth
                      size="small"
                      value={assistantPrompt}
                      onChange={(event) => setAssistantPrompt(event.target.value)}
                      placeholder="Ejemplo: quiero atender WhatsApp, validar KYC o conectar Twilio"
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

                <Alert severity="info" sx={{ borderRadius: 2 }}>
                  {systemHint}
                </Alert>
              </Stack>
            </Grid>

            <Grid item xs={12} md={5}>
              <Card variant="outlined" sx={{ borderRadius: 3, bgcolor: 'rgba(255,255,255,0.78)' }}>
                <CardContent>
                  <Stack spacing={2}>
                    <Box>
                      <Typography variant="h6">Estado rapido</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Lo minimo que necesitas para operar.
                      </Typography>
                    </Box>
                    <Grid container spacing={1.2}>
                      {readinessItems.map((item) => (
                        <Grid item xs={6} key={item.label}>
                          <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }}>
                            <Stack spacing={0.8}>
                              <Iconify icon={item.icon} width={22} sx={{ color: 'primary.main' }} />
                              <Typography variant="h5">{item.value}</Typography>
                              <Box>
                                <Typography variant="caption" fontWeight={700}>{item.label}</Typography>
                                <Typography variant="caption" color="text.secondary" display="block">
                                  {item.helper}
                                </Typography>
                              </Box>
                            </Stack>
                          </Paper>
                        </Grid>
                      ))}
                    </Grid>
                    <Chip
                      color={readyConnections > 0 ? 'success' : 'warning'}
                      label={readyConnections > 0 ? 'Listo para construir workflows' : 'Faltan integraciones'}
                      sx={{ alignSelf: 'flex-start' }}
                    />
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          </Grid>
        </Paper>

        <Grid container spacing={2.5} sx={{ mb: 3 }}>
          <Grid item xs={12} md={4}>
            <ActionCard
              title="Construir un workflow"
              helper="Define una intencion, selecciona un agente y agrega acciones como WhatsApp, API, KYC o pagos."
              icon="mdi:source-branch"
              href={paths.dashboard.workflows}
              cta="Abrir Workflow Studio"
            />
          </Grid>
          <Grid item xs={12} md={4}>
            <ActionCard
              title="Conectar canales"
              helper="Activa WhatsApp, Web Chat, voz o call center para recibir mensajes y llamadas reales."
              icon="mdi:message-processing-outline"
              href={paths.dashboard.system.channels}
              cta="Ver canales"
              color="success"
            />
          </Grid>
          <Grid item xs={12} md={4}>
            <ActionCard
              title="Agregar integraciones"
              helper="Configura Twilio, Storage, Drive, APIs o MCP una vez y reutilizalos en agentes y flujos."
              icon="mdi:connection"
              href={paths.dashboard.marketplace}
              cta="Ir a Marketplace"
              color="info"
            />
          </Grid>
        </Grid>

        <Grid container spacing={2.5}>
          <Grid item xs={12} md={4}>
            <Card variant="outlined" sx={{ borderRadius: 3 }}>
              <CardContent>
                <Typography variant="overline" color="text.secondary">Operacion</Typography>
                <Typography variant="h4">{metrics.completedToday}</Typography>
                <Typography variant="body2" color="text.secondary">
                  Ejecuciones completadas hoy.
                </Typography>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12} md={4}>
            <Card variant="outlined" sx={{ borderRadius: 3 }}>
              <CardContent>
                <Typography variant="overline" color="text.secondary">Agentes</Typography>
                <Typography variant="h4">{metrics.publishedAgents}/{metrics.totalAgents}</Typography>
                <Typography variant="body2" color="text.secondary">
                  Agentes publicados sobre el total configurado.
                </Typography>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12} md={4}>
            <Card variant="outlined" sx={{ borderRadius: 3 }}>
              <CardContent>
                <Typography variant="overline" color="text.secondary">Calidad</Typography>
                <Typography variant="h4">{Math.round(metrics.avgQualityScore * 100)}%</Typography>
                <Typography variant="body2" color="text.secondary">
                  Score promedio de respuestas recientes.
                </Typography>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
