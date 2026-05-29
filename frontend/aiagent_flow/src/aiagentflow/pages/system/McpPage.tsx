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
      setError(e?.message || 'No se pudieron cargar los conectores avanzados.');
    }
  }, [selectedServer]);

  useEffect(() => {
    void loadSettings();
    void loadServers();
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
      setError(e?.message || 'No se pudieron cargar las herramientas del conector.');
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
      setError(e?.response?.data?.message || e?.message || 'No se pudo ejecutar la herramienta.');
    } finally {
      setLoading(false);
    }
  };

  const enableMcp = async () => {
    setLoading(true);
    setError(null);
    try {
      await axios.post(`/api/v1/tenants/${tenantId}/mcp/enable`, {
        allowedServers: allowedServersCsv
          .split(',')
          .map((item) => item.trim())
          .filter(Boolean).length
          ? allowedServersCsv.split(',').map((item) => item.trim()).filter(Boolean)
          : selectedServer
            ? [selectedServer]
            : undefined,
        timeoutSeconds: 20,
        retryCount: 1,
      });
      await loadSettings();
    } catch (e: any) {
      setError(e?.message || 'No se pudo habilitar la conexion avanzada.');
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
      setError(e?.message || 'No se pudo guardar la configuracion del conector.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <Helmet>
        <title>Conectores avanzados | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between">
            <Box>
              <Typography variant="h4">Conectores avanzados</Typography>
              <Typography variant="body2" color="text.secondary">
                Habilita servidores externos, revisa sus herramientas y valida que puedan usarse desde asistentes y automatizaciones.
              </Typography>
            </Box>
            <Stack direction="row" spacing={1}>
              <Button variant="outlined" href={paths.dashboard.marketplace}>
                Configurar conexion
              </Button>
              <Button variant="contained" href={paths.dashboard.workflows}>
                Usar en automatizacion
              </Button>
            </Stack>
          </Stack>
        </Box>

        <Grid container spacing={2} sx={{ mb: 3 }}>
          {[
            ['1', 'Registra la conexion', 'Guarda el servidor, runtime y token como recurso del tenant.'],
            ['2', 'Permite servidores', 'Define que conexiones externas puede usar este tenant.'],
            ['3', 'Prueba herramientas', 'Valida una herramienta antes de publicarla en asistentes o automatizaciones.'],
            ['4', 'Activa el nodo', 'Workflow Studio mostrara si la conexion ya esta lista para usarse.'],
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
            ['Conexiones', servers.length, 'Configuradas'],
            ['Herramientas', tools.length, 'Disponibles'],
            ['Estado', settings?.enabled ? 'Activo' : 'Inactivo', 'Tenant'],
          ].map(([label, value, helper]) => (
            <Grid item xs={12} md={4} key={label}>
              <Card variant="outlined" sx={{ p: 2 }}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Box>
                    <Typography variant="h5">{String(value)}</Typography>
                    <Typography variant="caption" color="text.secondary">{label}</Typography>
                  </Box>
                  <Chip size="small" label={helper} variant="outlined" />
                </Stack>
              </Card>
            </Grid>
          ))}
        </Grid>

        {settings && (
          <Alert
            severity={settings.enabled ? 'success' : 'warning'}
            sx={{ mb: 2 }}
            action={
              !settings.enabled ? (
                <Button color="inherit" size="small" onClick={enableMcp} disabled={loading}>
                  Habilitar conexion
                </Button>
              ) : undefined
            }
          >
            Conexion avanzada: <b>{settings.enabled ? 'Activa' : 'Inactiva'}</b> · Runtime: <b>{settings.runtime}</b> · Timeout: {settings.timeoutSeconds}s · Reintentos: {settings.retryCount}
          </Alert>
        )}

        {error && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button color="inherit" size="small" onClick={() => void loadServers()}>Reintentar</Button>}
          >
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
                    label="Conexion"
                    value={selectedServer}
                    onChange={(e) => setSelectedServer(e.target.value)}
                    fullWidth
                  >
                    {servers.map((server) => (
                      <MenuItem key={server.name} value={server.name}>
                        {server.name} ({server.transport})
                      </MenuItem>
                    ))}
                  </TextField>

                  <Stack direction="row" spacing={1}>
                    <Button variant="outlined" onClick={() => void loadServers()} disabled={loading}>
                      Actualizar conexiones
                    </Button>
                    <Button
                      variant="outlined"
                      onClick={loadTools}
                      disabled={loading || !selectedServer || !settings?.enabled}
                    >
                      Cargar herramientas
                    </Button>
                  </Stack>

                  <TextField
                    label="Conexiones permitidas para este tenant"
                    value={allowedServersCsv}
                    onChange={(e) => setAllowedServersCsv(e.target.value)}
                    helperText="Separalas por coma. Solo estas conexiones podran usarse en automatizaciones."
                    fullWidth
                  />

                  <Button variant="outlined" onClick={saveSettings} disabled={loading}>
                    Guardar permisos
                  </Button>

                  <TextField
                    select
                    label="Herramienta"
                    value={selectedTool}
                    onChange={(e) => setSelectedTool(e.target.value)}
                    fullWidth
                  >
                    {tools.map((tool) => (
                      <MenuItem key={tool.name} value={tool.name}>{tool.name}</MenuItem>
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

                  <Button
                    variant="contained"
                    onClick={invoke}
                    disabled={loading || !selectedTool || !settings?.enabled}
                  >
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
