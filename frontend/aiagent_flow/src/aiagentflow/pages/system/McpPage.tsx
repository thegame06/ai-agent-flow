import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { paths } from 'src/routes/paths';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

interface McpServer {
  name: string;
  transport: string;
  url?: string;
  securityMode: string;
}

interface McpTool {
  name: string;
  description?: string;
  inputSchemaJson?: string;
}

interface TenantMcpSettings {
  tenantId: string;
  enabled: boolean;
  runtime: string;
  timeoutSeconds: number;
  retryCount: number;
  allowedServers: string[];
}

export default function McpPage() {
  const tenantId = useTenantId();
  const [servers, setServers] = useState<McpServer[]>([]);
  const [settings, setSettings] = useState<TenantMcpSettings | null>(null);
  const [selectedServer, setSelectedServer] = useState('');
  const [tools, setTools] = useState<McpTool[]>([]);
  const [selectedTool, setSelectedTool] = useState('');
  const [allowedServersCsv, setAllowedServersCsv] = useState('');
  const [inputJson, setInputJson] = useState('{}');
  const [output, setOutput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadSettings = useCallback(async () => {
    try {
      const res = await axios.get(`/api/v1/tenants/${tenantId}/mcp/settings`);
      setSettings(res.data);
      setAllowedServersCsv((res.data?.allowedServers ?? []).join(','));
    } catch {
      setSettings(null);
    }
  }, [tenantId]);
  
  const loadServers = useCallback(async () => {
    setError(null);
    try {
      const res = await axios.get('/api/v1/mcp/servers');
      setServers(res.data ?? []);
      if (res.data?.length && !selectedServer) setSelectedServer(res.data[0].name);
    } catch (e: any) {
      setError(e?.message || 'Failed to load MCP servers');
    }
  }, [selectedServer]);
  
  useEffect(() => {
    loadSettings();
    loadServers();
  }, [loadServers, loadSettings]);

  const loadTools = async () => {
    if (!selectedServer) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(`/api/v1/mcp/servers/${selectedServer}/tools`);
      const data = Array.isArray(res.data) ? res.data : [];
      setTools(data);
      if (data.length) {
        setSelectedTool(data[0].name);
        setInputJson(data[0].inputSchemaJson || '{}');
      }
    } catch (e: any) {
      setError(e?.message || 'Failed to load MCP tools');
      setTools([]);
    } finally {
      setLoading(false);
    }
  };

  const invoke = async () => {
    if (!selectedServer || !selectedTool) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(`/api/v1/mcp/servers/${selectedServer}/invoke`, {
        toolName: selectedTool,
        inputJson,
      });
      setOutput(JSON.stringify(res.data, null, 2));
    } catch (e: any) {
      setError(e?.response?.data?.message || e?.message || 'Failed to invoke MCP tool');
    } finally {
      setLoading(false);
    }
  };

  const enableMcp = async () => {
    setLoading(true);
    setError(null);
    try {
      await axios.post(`/api/v1/tenants/${tenantId}/mcp/enable`, {
        allowedServers: allowedServersCsv.split(',').map((item) => item.trim()).filter(Boolean).length ? allowedServersCsv.split(',').map((item) => item.trim()).filter(Boolean) : (selectedServer ? [selectedServer] : undefined),
        timeoutSeconds: 20,
        retryCount: 1,
      });
      await loadSettings();
    } catch (e: any) {
      setError(e?.message || 'Failed to enable MCP');
    } finally {
      setLoading(false);
    }
  };


  const saveSettings = async () => {
    setLoading(true);
    setError(null);
    try {
      await axios.put(`/api/v1/tenants/${tenantId}/mcp/settings`, {
        enabled: true,
        runtime: 'MicrosoftAgentFramework',
        timeoutSeconds: settings?.timeoutSeconds ?? 20,
        retryCount: settings?.retryCount ?? 1,
        allowedServers: allowedServersCsv
          .split(',')
          .map((item) => item.trim())
          .filter(Boolean),
      });
      await loadSettings();
    } catch (e: any) {
      setError(e?.message || 'Failed to save MCP settings');
    } finally {
      setLoading(false);
    }
  };
  return (
    <>
      <Helmet>
        <title>MCP para agentes | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between">
            <Box>
              <Typography variant="h4">MCP para agentes y workflows</Typography>
              <Typography variant="body2" color="text.secondary">
                Habilita servidores MCP, descubre herramientas y confirma que pueden usarse desde Agent Studio y Workflow Studio.
              </Typography>
            </Box>
            <Stack direction="row" spacing={1}>
              <Button variant="outlined" href={paths.dashboard.marketplace}>
                Configurar conexion
              </Button>
              <Button variant="contained" href={paths.dashboard.workflows}>
                Usar en workflow
              </Button>
            </Stack>
          </Stack>
        </Box>

        <Grid container spacing={2} sx={{ mb: 3 }}>
          {[
            ['1', 'Configura MCP en Marketplace', 'Guarda servidor, runtime y token como recurso del tenant.'],
            ['2', 'Permite servidores aqui', 'Define que servidores puede usar este tenant.'],
            ['3', 'Descubre y prueba tools', 'Valida una tool antes de publicarla en agentes o workflows.'],
            ['4', 'Usa el nodo MCP', 'Workflow Studio mostrara si la conexion esta lista.'],
          ].map(([step, title, body]) => (
            <Grid key={step} item xs={12} md={3}>
              <Card variant="outlined" sx={{ height: '100%' }}>
                <CardContent>
                  <Stack spacing={1}>
                    <Chip label={step} sx={{ width: 32 }} />
                    <Typography variant="subtitle2">{title}</Typography>
                    <Typography variant="caption" color="text.secondary">{body}</Typography>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>

        <Grid container spacing={2} sx={{ mb: 3 }}>
          {[
            ['Servidores', servers.length, 'Configurados'],
            ['Herramientas', tools.length, 'Descubiertas'],
            ['Estado', settings?.enabled ? 'Activo' : 'Inactivo', 'Tenant'],
          ].map(([label, value, helper]) => (
            <Grid item xs={12} md={4} key={label}>
              <Card variant="outlined" sx={{ p: 2 }}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Box>
                    <Typography variant="h5">{String(value)}</Typography>
                    <Typography variant="caption" color="text.secondary">{label}</Typography>
                  </Box>
                  <Chip size="small" label={helper} variant="soft" />
                </Stack>
              </Card>
            </Grid>
          ))}
        </Grid>

        {settings && (
          <Alert severity={settings.enabled ? 'success' : 'warning'} sx={{ mb: 2 }}
            action={
              !settings.enabled ? (
                <Button color="inherit" size="small" onClick={enableMcp} disabled={loading}>
                  Habilitar MCP
                </Button>
              ) : undefined
            }
          >
            MCP: <b>{settings.enabled ? 'Activo' : 'Inactivo'}</b> · Runtime: <b>{settings.runtime}</b> · Timeout: {settings.timeoutSeconds}s · Reintentos: {settings.retryCount}
          </Alert>
        )}

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} action={<Button color="inherit" size="small" onClick={loadServers}>Retry</Button>}>
            {error}
          </Alert>
        )}

        <Grid container spacing={3}>
          <Grid item xs={12} md={5}>
            <Card>
              <CardContent>
                <Stack spacing={2}>
                  <TextField
                    select
                    label="Servidor MCP"
                    value={selectedServer}
                    onChange={(e) => setSelectedServer(e.target.value)}
                    fullWidth
                  >
                    {servers.map((s) => (
                      <MenuItem key={s.name} value={s.name}>{s.name} ({s.transport})</MenuItem>
                    ))}
                  </TextField>

                  <Stack direction="row" spacing={1}>
                    <Button variant="outlined" onClick={loadServers} disabled={loading}>
                      Actualizar servidores
                    </Button>
                    <Button variant="outlined" onClick={loadTools} disabled={loading || !selectedServer || !settings?.enabled}>
                      Descubrir herramientas
                    </Button>
                  </Stack>


                  <TextField
                    label="Servidores permitidos para este tenant"
                    value={allowedServersCsv}
                    onChange={(e) => setAllowedServersCsv(e.target.value)}
                    helperText="Separados por coma. Workflow Studio solo podra usar servidores permitidos."
                    fullWidth
                  />

                  <Button variant="outlined" onClick={saveSettings} disabled={loading}>
                    Guardar permisos MCP
                  </Button>
                  <TextField
                    select
                    label="Herramienta"
                    value={selectedTool}
                    onChange={(e) => setSelectedTool(e.target.value)}
                    fullWidth
                  >
                    {tools.map((t) => (
                      <MenuItem key={t.name} value={t.name}>{t.name}</MenuItem>
                    ))}
                  </TextField>

                  <TextField
                    label="Entrada JSON"
                    value={inputJson}
                    onChange={(e) => setInputJson(e.target.value)}
                    fullWidth
                    multiline
                    minRows={8}
                  />

                  <Button variant="contained" onClick={invoke} disabled={loading || !selectedTool || !settings?.enabled}>
                    Probar herramienta
                  </Button>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={7}>
            <Card>
              <CardContent>
                <Typography variant="subtitle1" sx={{ mb: 1 }}>Resultado</Typography>
                <TextField value={output} fullWidth multiline minRows={22} InputProps={{ readOnly: true }} />
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}

