import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
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

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

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

export default function SettingsPage() {
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

      <DashboardContent maxWidth="lg">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Configuracion general</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Administra la configuracion del tenant, seguridad, limites de ejecucion y preferencias de la plataforma.
          </Typography>
        </Box>

        {message && <Alert severity={message.includes('correctamente') ? 'success' : 'error'} sx={{ mb: 2 }}>{message}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Configuracion del tenant" subheader="Datos generales" avatar={<Iconify icon="mdi:domain" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <TextField fullWidth label="Nombre del tenant" value={settings.tenantName} onChange={(e) => set('tenantName', e.target.value)} disabled={loading} />
                  <TextField fullWidth label="Tenant ID" value={tenantId} disabled />
                  <TextField fullWidth label="Version API por defecto" value={settings.defaultApiVersion} onChange={(e) => set('defaultApiVersion', e.target.value)} disabled={loading} />
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
                  <RowSwitch label="Forzar RBAC" checked={settings.enforceRbac} onChange={(v) => set('enforceRbac', v)} />
                  <Divider />
                  <RowSwitch label="Proteccion contra prompt injection" checked={settings.promptInjectionGuard} onChange={(v) => set('promptInjectionGuard', v)} />
                  <Divider />
                  <RowSwitch label="Aislar herramientas peligrosas" checked={settings.sandboxDangerousTools} onChange={(v) => set('sandboxDangerousTools', v)} />
                  <Divider />
                  <RowSwitch label="Registrar auditoria" checked={settings.auditLogging} onChange={(v) => set('auditLogging', v)} />
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
                  <RowSwitch label="Modo oscuro" checked={darkModeEnabled} onChange={toggleDarkMode} />
                  <Typography variant="caption" color="text.secondary">
                    Aplica a toda la interfaz del dashboard y queda guardado en el navegador.
                  </Typography>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Limites de ejecucion" subheader="Restricciones del runtime" avatar={<Iconify icon="mdi:speedometer" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <TextField fullWidth label="Maximo de pasos por ejecucion" type="number" value={settings.maxStepsPerExecution} onChange={(e) => set('maxStepsPerExecution', Number(e.target.value))} />
                  <TextField fullWidth label="Timeout por paso (segundos)" type="number" value={settings.timeoutPerStepSeconds} onChange={(e) => set('timeoutPerStepSeconds', Number(e.target.value))} />
                  <TextField fullWidth label="Maximo de tokens por ejecucion" type="number" value={settings.maxTokensPerExecution} onChange={(e) => set('maxTokensPerExecution', Number(e.target.value))} />
                  <TextField fullWidth label="Maximo de ejecuciones concurrentes" type="number" value={settings.maxConcurrentExecutions} onChange={(e) => set('maxConcurrentExecutions', Number(e.target.value))} />
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
                  <RowSwitch label="Exportar OpenTelemetry" checked={settings.otlpExport} onChange={(v) => set('otlpExport', v)} />
                  <Divider />
                  <TextField fullWidth label="Endpoint OTLP" value={settings.otlpEndpoint} onChange={(e) => set('otlpEndpoint', e.target.value)} size="small" />
                  <RowSwitch label="Execution replay" checked={settings.executionReplay} onChange={(v) => set('executionReplay', v)} />
                  <Divider />
                  <RowSwitch label="Registrar decisiones LLM" checked={settings.llmDecisionLogging} onChange={(v) => set('llmDecisionLogging', v)} />
                </Stack>
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

function RowSwitch({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Typography variant="subtitle2">{label}</Typography>
      <Switch checked={checked} onChange={(e) => onChange(e.target.checked)} />
    </Box>
  );
}
