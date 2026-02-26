import { Helmet } from 'react-helmet-async';
import { useEffect, useState } from 'react';

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

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

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
        setMessage(e?.message || 'Failed to load settings');
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [tenantId]);

  const save = async () => {
    setSaving(true);
    setMessage(null);
    try {
      const res = await axios.put(`/api/v1/tenants/${tenantId}/settings`, settings);
      setSettings({ ...defaultSettings, ...res.data });
      setMessage('Settings saved successfully');
    } catch (e: any) {
      setMessage(e?.message || 'Failed to save settings');
    } finally {
      setSaving(false);
    }
  };

  const set = <K extends keyof Settings>(key: K, value: Settings[K]) =>
    setSettings((prev) => ({ ...prev, [key]: value }));

  return (
    <>
      <Helmet>
        <title>Settings | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Platform Settings</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Manage your tenant configuration, security settings, and platform preferences.
          </Typography>
        </Box>

        {message && <Alert severity={message.includes('successfully') ? 'success' : 'error'} sx={{ mb: 2 }}>{message}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Tenant Configuration" subheader="General settings" avatar={<Iconify icon="mdi:domain" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <TextField fullWidth label="Tenant Name" value={settings.tenantName} onChange={(e) => set('tenantName', e.target.value)} disabled={loading} />
                  <TextField fullWidth label="Tenant ID" value={tenantId} disabled />
                  <TextField fullWidth label="Default API Version" value={settings.defaultApiVersion} onChange={(e) => set('defaultApiVersion', e.target.value)} disabled={loading} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Security & Compliance" subheader="Execution controls" avatar={<Iconify icon="mdi:shield-lock-outline" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <RowSwitch label="Enforce RBAC" checked={settings.enforceRbac} onChange={(v) => set('enforceRbac', v)} />
                  <Divider />
                  <RowSwitch label="Prompt Injection Guard" checked={settings.promptInjectionGuard} onChange={(v) => set('promptInjectionGuard', v)} />
                  <Divider />
                  <RowSwitch label="Sandbox Dangerous Tools" checked={settings.sandboxDangerousTools} onChange={(v) => set('sandboxDangerousTools', v)} />
                  <Divider />
                  <RowSwitch label="Audit Logging" checked={settings.auditLogging} onChange={(v) => set('auditLogging', v)} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Execution Limits" subheader="Runtime boundaries" avatar={<Iconify icon="mdi:speedometer" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <TextField fullWidth label="Max Steps per Execution" type="number" value={settings.maxStepsPerExecution} onChange={(e) => set('maxStepsPerExecution', Number(e.target.value))} />
                  <TextField fullWidth label="Timeout per Step (seconds)" type="number" value={settings.timeoutPerStepSeconds} onChange={(e) => set('timeoutPerStepSeconds', Number(e.target.value))} />
                  <TextField fullWidth label="Max Tokens per Execution" type="number" value={settings.maxTokensPerExecution} onChange={(e) => set('maxTokensPerExecution', Number(e.target.value))} />
                  <TextField fullWidth label="Max Concurrent Executions" type="number" value={settings.maxConcurrentExecutions} onChange={(e) => set('maxConcurrentExecutions', Number(e.target.value))} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader title="Observability" subheader="Telemetry and tracing" avatar={<Iconify icon="mdi:chart-timeline-variant" width={28} />} />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <RowSwitch label="OpenTelemetry Export" checked={settings.otlpExport} onChange={(v) => set('otlpExport', v)} />
                  <Divider />
                  <TextField fullWidth label="OTLP Endpoint" value={settings.otlpEndpoint} onChange={(e) => set('otlpEndpoint', e.target.value)} size="small" />
                  <RowSwitch label="Execution Replay" checked={settings.executionReplay} onChange={(v) => set('executionReplay', v)} />
                  <Divider />
                  <RowSwitch label="LLM Decision Logging" checked={settings.llmDecisionLogging} onChange={(v) => set('llmDecisionLogging', v)} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        </Grid>

        <Box sx={{ mt: 4, display: 'flex', justifyContent: 'flex-end' }}>
          <Button variant="contained" size="large" startIcon={<Iconify icon="mdi:content-save-outline" />} onClick={save} disabled={loading || saving}>
            {saving ? 'Saving...' : 'Save Settings'}
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
