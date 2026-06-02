import type { ReactNode } from 'react';

import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Switch from '@mui/material/Switch';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import CardHeader from '@mui/material/CardHeader';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import { useColorScheme } from '@mui/material/styles';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { useSettingsWorkspace } from 'src/aiagentflow/pages/settings/SettingsWorkspaceContext';

import { Iconify } from 'src/components/iconify';
import { useSettingsContext } from 'src/components/settings';

type Settings = {
  tenantName: string;
  defaultApiVersion: string;
  enforceRbac: boolean;
  promptInjectionGuard: boolean;
  sandboxDangerousTools: boolean;
  auditLogging: boolean;
  maxStepsPerExecution: number;
  timeoutPerStepSeconds: number;
  maxTokensPerExecution: number;
  maxConcurrentExecutions: number;
  otlpExport: boolean;
  otlpEndpoint: string;
  executionReplay: boolean;
  llmDecisionLogging: boolean;
};

type CoverageState = 'active' | 'partial' | 'ui' | 'stored';

const defaultSettings: Settings = {
  tenantName: 'Tenant',
  defaultApiVersion: 'v1',
  enforceRbac: true,
  promptInjectionGuard: true,
  sandboxDangerousTools: true,
  auditLogging: true,
  maxStepsPerExecution: 25,
  timeoutPerStepSeconds: 30,
  maxTokensPerExecution: 100000,
  maxConcurrentExecutions: 10,
  otlpExport: true,
  otlpEndpoint: 'http://localhost:4317',
  executionReplay: true,
  llmDecisionLogging: true,
};

const coverageLabels: Record<CoverageState, { label: string; color: 'success' | 'warning' | 'info' | 'default' }> = {
  active: { label: 'Activo', color: 'success' },
  partial: { label: 'Parcial', color: 'warning' },
  ui: { label: 'Solo UI', color: 'info' },
  stored: { label: 'No conectado', color: 'default' },
};

const settingCoverage: Record<keyof Settings, { state: CoverageState; detail: string }> = {
  tenantName: { state: 'partial', detail: 'Se usa como dato administrativo, no como control fuerte de runtime.' },
  defaultApiVersion: { state: 'stored', detail: 'Se persiste, pero hoy no gobierna rutas ni versionado del backend.' },
  enforceRbac: { state: 'stored', detail: 'El RBAC real vive en middleware y permisos; este switch no lo gobierna todavia.' },
  promptInjectionGuard: { state: 'active', detail: 'Gobierna el runtime para bloquear intentos de prompt injection antes del loop y en salidas del LLM.' },
  sandboxDangerousTools: { state: 'active', detail: 'Gobierna si las tools de alto riesgo se ejecutan aisladas en sandbox.' },
  auditLogging: { state: 'partial', detail: 'Hay auditoria activa en varias rutas, pero este switch aun no la apaga o prende de forma central.' },
  maxStepsPerExecution: { state: 'stored', detail: 'Se guarda, pero el runtime actual no lo toma como limite global del tenant.' },
  timeoutPerStepSeconds: { state: 'stored', detail: 'Se guarda, pero los timeouts efectivos vienen de cada ruta o componente.' },
  maxTokensPerExecution: { state: 'stored', detail: 'Hoy predominan limites por agente o ejecucion, no este valor global del tenant.' },
  maxConcurrentExecutions: { state: 'stored', detail: 'Se persiste, pero no hay enforcement global de concurrencia amarrado a este campo.' },
  otlpExport: { state: 'partial', detail: 'La telemetria existe, pero su wiring principal sigue viniendo de appsettings del servidor.' },
  otlpEndpoint: { state: 'partial', detail: 'Se guarda por tenant, pero el exporter actual toma principalmente configuracion del host.' },
  executionReplay: { state: 'stored', detail: 'Se persiste, pero no habilita ni deshabilita el replay de forma efectiva hoy.' },
  llmDecisionLogging: { state: 'stored', detail: 'Se guarda, pero no controla todavia todas las trazas de decision del runtime.' },
};

const movedSections = [
  { label: 'Contextos MD', path: paths.dashboard.settings.agentContexts, helper: 'Markdown por rol y canal para agentes.' },
  { label: 'Modelos IA', path: paths.dashboard.settings.models, helper: 'Catalogo, perfiles y runtime models.' },
  { label: 'Credenciales', path: paths.dashboard.settings.authProfiles, helper: 'Perfiles de autenticacion por proveedor.' },
  { label: 'Funciones beta', path: paths.dashboard.settings.featureFlags, helper: 'Flags experimentales y rollout.' },
  { label: 'Politicas', path: paths.dashboard.settings.policies, helper: 'Gobernanza y reglas operativas.' },
  { label: 'Equipos y atencion', path: paths.dashboard.settings.workforce, helper: 'Colas, responsables y handoff humano.' },
];

export default function SettingsPage() {
  const { embedded } = useSettingsWorkspace();
  const uiSettings = useSettingsContext();
  const { mode, setMode } = useColorScheme();
  const tenantId = useTenantId();
  const [settings, setSettings] = useState<Settings>(defaultSettings);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      setMessage(null);
      try {
        const res = await axios.get(`/api/v1/tenants/${tenantId}/settings`);
        setSettings({ ...defaultSettings, ...res.data });
      } catch (e: any) {
        setMessage(e?.message || 'No se pudo cargar la configuracion.');
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [tenantId]);

  const save = async () => {
    setSaving(true);
    setMessage(null);
    try {
      const res = await axios.put(`/api/v1/tenants/${tenantId}/settings`, settings);
      setSettings({ ...defaultSettings, ...res.data });
      setMessage('Configuracion guardada correctamente.');
    } catch (e: any) {
      setMessage(e?.message || 'No se pudo guardar la configuracion.');
    } finally {
      setSaving(false);
    }
  };

  const set = <K extends keyof Settings>(key: K, value: Settings[K]) =>
    setSettings((prev) => ({ ...prev, [key]: value }));

  const darkModeEnabled = (mode ?? uiSettings.state.colorScheme) === 'dark';

  const toggleDarkMode = (enabled: boolean) => {
    const next = enabled ? 'dark' : 'light';
    setMode(next);
    uiSettings.setState({ colorScheme: next });
  };

  return (
    <>
      <Helmet>
        <title>Configuracion general | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg" disablePadding={embedded}>
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Configuracion general</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Administra la configuracion del tenant, seguridad, limites de ejecucion y preferencias de la plataforma.
          </Typography>
        </Box>

        {message && <Alert severity={message.includes('correctamente') ? 'success' : 'error'} sx={{ mb: 2 }}>{message}</Alert>}

        <Alert severity="info" sx={{ mb: 3 }}>
          Esta vista solo deja visibles configuraciones activas o parciales. Los settings legacy que hoy solo se guardan y no gobiernan el runtime fueron ocultados para evitar falsa sensacion de control.
        </Alert>

        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Configuracion del tenant" subheader="Datos generales" avatar={<Iconify icon="mdi:domain" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <SettingField
                    label="Nombre del tenant"
                    coverage={settingCoverage.tenantName}
                    control={<TextField fullWidth label="Nombre del tenant" value={settings.tenantName} onChange={(e) => set('tenantName', e.target.value)} disabled={loading} />}
                  />
                  <TextField fullWidth label="Tenant ID" value={tenantId} disabled />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Seguridad y cumplimiento" subheader="Controles de ejecucion" avatar={<Iconify icon="mdi:shield-lock-outline" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <RowSwitch label="Proteccion contra prompt injection" checked={settings.promptInjectionGuard} onChange={(v) => set('promptInjectionGuard', v)} coverage={settingCoverage.promptInjectionGuard} />
                  <Divider />
                  <RowSwitch label="Aislar herramientas peligrosas" checked={settings.sandboxDangerousTools} onChange={(v) => set('sandboxDangerousTools', v)} coverage={settingCoverage.sandboxDangerousTools} />
                  <Divider />
                  <RowSwitch label="Registrar auditoria" checked={settings.auditLogging} onChange={(v) => set('auditLogging', v)} coverage={settingCoverage.auditLogging} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Apariencia" subheader="Preferencias visuales" avatar={<Iconify icon="mdi:theme-light-dark" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <RowSwitch label="Modo oscuro" checked={darkModeEnabled} onChange={toggleDarkMode} coverage={{ state: 'ui', detail: 'Afecta solo la interfaz del dashboard en este navegador.' }} />
                  <Typography variant="caption" color="text.secondary">
                    Aplica a toda la interfaz del dashboard y queda guardado en el navegador.
                  </Typography>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Observabilidad" subheader="Telemetria y trazas" avatar={<Iconify icon="mdi:chart-timeline-variant" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <RowSwitch label="Exportar OpenTelemetry" checked={settings.otlpExport} onChange={(v) => set('otlpExport', v)} coverage={settingCoverage.otlpExport} />
                  <Divider />
                  <SettingField
                    label="Endpoint OTLP"
                    coverage={settingCoverage.otlpEndpoint}
                    control={<TextField fullWidth label="Endpoint OTLP" value={settings.otlpEndpoint} onChange={(e) => set('otlpEndpoint', e.target.value)} size="small" />}
                  />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card>
              <CardHeader title="Configuracion especializada" subheader="Se movio a su dominio natural para evitar mezclar runtime real con placeholders." avatar={<Iconify icon="mdi:tune-variant" width={28} />} />
              <Divider />
              <CardContent>
                <Grid container spacing={2}>
                  {movedSections.map((section) => (
                    <Grid item xs={12} md={6} key={section.path}>
                      <Card variant="outlined" sx={{ borderRadius: 2 }}>
                        <CardContent>
                          <Stack spacing={1.5}>
                            <Typography variant="subtitle2">{section.label}</Typography>
                            <Typography variant="body2" color="text.secondary">
                              {section.helper}
                            </Typography>
                            <Box>
                              <Button component={RouterLink} href={section.path} size="small" variant="outlined">
                                Abrir seccion
                              </Button>
                            </Box>
                          </Stack>
                        </CardContent>
                      </Card>
                    </Grid>
                  ))}
                </Grid>
              </CardContent>
            </Card>
          </Grid>
        </Grid>

        <Box sx={{ mt: 4, display: 'flex', justifyContent: 'flex-end' }}>
          <Button variant="contained" size="large" startIcon={<Iconify icon="mdi:content-save-outline" />} onClick={save} disabled={loading || saving}>
            {saving ? 'Guardando...' : 'Guardar configuracion'}
          </Button>
        </Box>
      </DashboardContent>
    </>
  );
}

function RowSwitch({
  label,
  checked,
  onChange,
  coverage,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  coverage: { state: CoverageState; detail: string };
}) {
  const meta = coverageLabels[coverage.state];
  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2 }}>
        <Box>
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography variant="subtitle2">{label}</Typography>
            <Chip size="small" label={meta.label} color={meta.color} variant={meta.color === 'default' ? 'outlined' : 'filled'} />
          </Stack>
          <Typography variant="caption" color="text.secondary">
            {coverage.detail}
          </Typography>
        </Box>
        <Switch checked={checked} onChange={(e) => onChange(e.target.checked)} />
      </Box>
    </Box>
  );
}

function SettingField({
  label,
  coverage,
  control,
}: {
  label: string;
  coverage: { state: CoverageState; detail: string };
  control: ReactNode;
}) {
  const meta = coverageLabels[coverage.state];
  return (
    <Stack spacing={1}>
      <Stack direction="row" spacing={1} alignItems="center">
        <Typography variant="subtitle2">{label}</Typography>
        <Chip size="small" label={meta.label} color={meta.color} variant={meta.color === 'default' ? 'outlined' : 'filled'} />
      </Stack>
      <Typography variant="caption" color="text.secondary">
        {coverage.detail}
      </Typography>
      {control}
    </Stack>
  );
}
