import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import CardContent from '@mui/material/CardContent';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

type Entry = {
  extensionId: string;
  name: string;
  version: string;
  description: string;
  source: string;
  metadata: {
    vendor: string;
    permissions: string[];
    riskLevel: string;
    compatibility: string;
    signatureValid: boolean;
    isQuarantined: boolean;
    quarantineReason?: string;
  };
};

type TenantConnection = {
  id: string;
  name: string;
  type: 'Rest' | 'Messaging' | 'Storage' | 'Mcp' | 'Sheets' | string;
  connectorId: string;
  config: Record<string, string>;
  secretVersion?: number;
  secretRotatedAt?: string;
  secretExpiresAt?: string;
};

type ConnectionResource = {
  id: string;
  name: string;
  type: string;
  connectorId: string;
  ready: boolean;
  capabilities: string[];
  suggestedNodes: string[];
  requiredConfigKeys: string[];
  secretRequired: boolean;
  checks: Array<{ check: string; status: string; detail: string }>;
};

const CAPABILITY_LABELS: Record<string, string> = {
  'voice.call': 'Llamadas de voz',
  'callcenter.outbound_call': 'Call center saliente',
  sms: 'SMS',
  'status callbacks': 'Estados de entrega',
  'http.request': 'Consultar API',
  'webhook.call': 'Llamar webhook',
  'files.read': 'Leer archivos',
  'drive.lookup': 'Buscar en Drive',
  'storage.write': 'Guardar documentos',
  'mcp.tool_call': 'Usar herramientas MCP',
  'tool discovery': 'Descubrir tools',
};

const NODE_LABELS: Record<string, string> = {
  'voice.call': 'Llamada de voz',
  'callcenter.outbound_call': 'Llamada de call center',
  'connect.enqueue_campaign_message': 'Mensaje saliente',
  'files.read': 'Leer archivo',
  'drive.lookup': 'Buscar en Drive',
  'storage.write': 'Guardar archivo',
  'mcp.tool_call': 'Tool MCP',
  'http.request': 'Consultar API',
  'webhook.call': 'Webhook',
};

const QUICK_CONNECTIONS = [
  {
    id: 'twilio',
    title: 'Twilio omnicanal',
    type: 'Messaging',
    connectorId: 'twilio',
    icon: 'mdi:phone-in-talk-outline',
    description: 'Una sola configuracion para voz, call center, SMS y futuros canales WhatsApp por Twilio.',
    config: { provider: 'twilio', accountSid: '', fromPhoneNumber: '', statusCallbackUrl: '' },
    secretHint: '{"authToken":"..."}',
    capabilities: ['voice.call', 'callcenter.outbound_call', 'sms', 'status callbacks'],
  },
  {
    id: 'rest-api',
    title: 'API / Webhook',
    type: 'Rest',
    connectorId: 'rest-api',
    icon: 'mdi:api',
    description: 'Conexion reusable para nodos Consultar API y Llamar webhook.',
    config: { baseUrl: '', authType: 'bearer' },
    secretHint: '{"bearerToken":"..."}',
    capabilities: ['http.request', 'webhook.call'],
  },
  {
    id: 'storage',
    title: 'Storage / Archivos',
    type: 'Storage',
    connectorId: 'storage',
    icon: 'mdi:folder-file-outline',
    description: 'Repositorio para archivos, Drive sincronizado, Excel y resultados de workflow.',
    config: { provider: 'internal', bucket: 'default' },
    secretHint: '',
    capabilities: ['files.read', 'drive.lookup', 'storage.write'],
  },
  {
    id: 'mcp',
    title: 'MCP Tools',
    type: 'Mcp',
    connectorId: 'mcp',
    icon: 'mdi:connection',
    description: 'Publica servidores MCP para agentes y Workflow Studio.',
    config: { server: 'local-test', runtime: 'MicrosoftAgentFramework' },
    secretHint: '{"token":"..."}',
    capabilities: ['mcp.tool_call', 'tool discovery'],
  },
] as const;

export default function MarketplacePage() {
  const tenantId = useTenantId();
  const [query, setQuery] = useState('');
  const [entries, setEntries] = useState<Entry[]>([]);
  const [installed, setInstalled] = useState<Record<string, boolean>>({});
  const [connections, setConnections] = useState<TenantConnection[]>([]);
  const [resources, setResources] = useState<ConnectionResource[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [healthMessage, setHealthMessage] = useState<string | null>(null);
  const [openConnection, setOpenConnection] = useState(false);
  const [connectionForm, setConnectionForm] = useState<{
    id: string;
    name: string;
    type: string;
    connectorId: string;
    configJson: string;
    secret: string;
  }>({
    id: 'twilio-main',
    name: 'Twilio principal',
    type: 'Messaging',
    connectorId: 'twilio',
    configJson: JSON.stringify(QUICK_CONNECTIONS[0].config, null, 2),
    secret: QUICK_CONNECTIONS[0].secretHint,
  });

  const load = useCallback(async () => {
    setError(null);
    try {
      const [catalogRes, statesRes, connectionsRes, resourcesRes] = await Promise.all([
        axios.get('/api/v1/extensions/catalog', { params: query ? { q: query } : {} }),
        axios.get(`/api/v1/extensions/tenants/${tenantId}/states`),
        axios.get(endpoints.agentflow.connections.list(tenantId)).catch(() => ({ data: [] })),
        axios.get(endpoints.agentflow.connections.resources(tenantId)).catch(() => ({ data: [] })),
      ]);

      setEntries(catalogRes.data ?? []);
      setInstalled(statesRes.data ?? {});
      setConnections(connectionsRes.data ?? []);
      setResources(resourcesRes.data ?? []);
    } catch (err: any) {
      setError(err?.message || 'Error cargando marketplace');
    }
  }, [query, tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  const visibleEntries = useMemo(
    () =>
      entries.filter(
        (e) =>
          !query ||
          `${e.name} ${e.extensionId} ${e.metadata.vendor} ${(e.metadata.permissions || []).join(' ')}`
            .toLowerCase()
            .includes(query.toLowerCase())
      ),
    [entries, query]
  );

  const installedCount = Object.values(installed).filter(Boolean).length;
  const readyResources = resources.filter((resource) => resource.ready).length;
  const configuredConnectionIds = new Set(connections.map((connection) => connection.connectorId));

  const configuredByConnectorId = (connectorId: string) =>
    connections.find((connection) => connection.connectorId === connectorId);

  const openQuickConnection = (preset: (typeof QUICK_CONNECTIONS)[number]) => {
    setConnectionForm({
      id: preset.id === 'twilio' ? 'twilio-main' : `${preset.id}-main`,
      name: preset.title,
      type: preset.type,
      connectorId: preset.connectorId,
      configJson: JSON.stringify(preset.config, null, 2),
      secret: preset.secretHint,
    });
    setOpenConnection(true);
  };

  const readConfigValue = (key: string) => {
    try {
      const parsed = JSON.parse(connectionForm.configJson || '{}') as Record<string, string>;
      return parsed[key] ?? '';
    } catch {
      return '';
    }
  };

  const readSecretValue = (key: string) => {
    try {
      const parsed = JSON.parse(connectionForm.secret || '{}') as Record<string, string>;
      return parsed[key] ?? '';
    } catch {
      return connectionForm.secret;
    }
  };

  const updateConfigValue = (key: string, value: string) => {
    let parsed: Record<string, string> = {};
    try {
      parsed = JSON.parse(connectionForm.configJson || '{}') as Record<string, string>;
    } catch {
      parsed = {};
    }
    parsed[key] = value;
    setConnectionForm((prev) => ({ ...prev, configJson: JSON.stringify(parsed, null, 2) }));
  };

  const renderGuidedConnectionFields = () => {
    if (connectionForm.connectorId === 'twilio') {
      return (
        <Card variant="outlined" sx={{ p: 2, bgcolor: 'background.neutral' }}>
          <Stack spacing={1.5}>
            <Box>
              <Typography variant="subtitle2">Twilio omnicanal</Typography>
              <Typography variant="caption" color="text.secondary">
                Esta conexion vive en backend por tenant. Luego canales de voz, call center, SMS o WhatsApp por Twilio solo referencian este ID.
              </Typography>
            </Box>
            <TextField
              label="Account SID"
              value={readConfigValue('accountSid')}
              onChange={(e) => updateConfigValue('accountSid', e.target.value)}
              fullWidth
            />
            <TextField
              label="Numero origen"
              placeholder="+15551234567"
              value={readConfigValue('fromPhoneNumber')}
              onChange={(e) => updateConfigValue('fromPhoneNumber', e.target.value)}
              fullWidth
            />
            <TextField
              label="Status callback URL"
              value={readConfigValue('statusCallbackUrl')}
              onChange={(e) => updateConfigValue('statusCallbackUrl', e.target.value)}
              fullWidth
            />
            <TextField
              label="Auth token"
              value={readSecretValue('authToken')}
              onChange={(e) =>
                setConnectionForm((prev) => ({ ...prev, secret: JSON.stringify({ authToken: e.target.value }, null, 2) }))
              }
              fullWidth
              type="password"
              helperText="Se guarda cifrado/protegido en backend; no queda en el canal ni en el nodo."
            />
          </Stack>
        </Card>
      );
    }

    if (connectionForm.connectorId === 'storage') {
      return (
        <Card variant="outlined" sx={{ p: 2, bgcolor: 'background.neutral' }}>
          <Stack spacing={1.5}>
            <Typography variant="subtitle2">Storage / Drive / archivos</Typography>
            <TextField
              select
              label="Proveedor"
              value={readConfigValue('provider') || 'internal'}
              onChange={(e) => updateConfigValue('provider', e.target.value)}
              fullWidth
            >
              {['internal', 'google-drive', 's3', 'azure-blob'].map((provider) => (
                <MenuItem key={provider} value={provider}>
                  {provider}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Bucket, carpeta o workspace"
              value={readConfigValue('bucket')}
              onChange={(e) => updateConfigValue('bucket', e.target.value)}
              fullWidth
            />
            <TextField
              label="Ruta base"
              value={readConfigValue('basePath')}
              onChange={(e) => updateConfigValue('basePath', e.target.value)}
              fullWidth
            />
          </Stack>
        </Card>
      );
    }

    if (connectionForm.connectorId === 'mcp') {
      return (
        <Card variant="outlined" sx={{ p: 2, bgcolor: 'background.neutral' }}>
          <Stack spacing={1.5}>
            <Typography variant="subtitle2">Servidor MCP</Typography>
            <TextField
              label="Servidor"
              value={readConfigValue('server')}
              onChange={(e) => updateConfigValue('server', e.target.value)}
              fullWidth
            />
            <TextField
              label="Runtime"
              value={readConfigValue('runtime')}
              onChange={(e) => updateConfigValue('runtime', e.target.value)}
              fullWidth
            />
            <TextField
              label="Token o secret"
              value={readSecretValue('token')}
              onChange={(e) =>
                setConnectionForm((prev) => ({ ...prev, secret: e.target.value ? JSON.stringify({ token: e.target.value }, null, 2) : '' }))
              }
              fullWidth
              type="password"
            />
          </Stack>
        </Card>
      );
    }

    if (connectionForm.connectorId === 'rest-api') {
      return (
        <Card variant="outlined" sx={{ p: 2, bgcolor: 'background.neutral' }}>
          <Stack spacing={1.5}>
            <Typography variant="subtitle2">API / Webhook reusable</Typography>
            <TextField
              label="Base URL"
              value={readConfigValue('baseUrl')}
              onChange={(e) => updateConfigValue('baseUrl', e.target.value)}
              fullWidth
            />
            <TextField
              select
              label="Autenticacion"
              value={readConfigValue('authType') || 'bearer'}
              onChange={(e) => updateConfigValue('authType', e.target.value)}
              fullWidth
            >
              {['none', 'bearer', 'api-key', 'basic'].map((authType) => (
                <MenuItem key={authType} value={authType}>
                  {authType}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Token/API key"
              value={readSecretValue('bearerToken')}
              onChange={(e) =>
                setConnectionForm((prev) => ({ ...prev, secret: e.target.value ? JSON.stringify({ bearerToken: e.target.value }, null, 2) : '' }))
              }
              fullWidth
              type="password"
            />
          </Stack>
        </Card>
      );
    }

    return null;
  };

  const saveConnection = async () => {
    let config: Record<string, string>;
    try {
      config = JSON.parse(connectionForm.configJson || '{}');
    } catch {
      setError('La configuracion debe ser JSON valido.');
      return;
    }

    await axios.put(endpoints.agentflow.connections.upsert(tenantId, connectionForm.id), {
      name: connectionForm.name,
      type: connectionForm.type,
      connectorId: connectionForm.connectorId,
      config,
      allowedAgentIds: [],
      allowedNodeIds: [],
      allowedConnectorIds: [],
    });

    if (connectionForm.secret.trim()) {
      await axios.post(endpoints.agentflow.connections.secret(tenantId, connectionForm.id), {
        secret: connectionForm.secret,
        expiresAt: null,
      });
    }

    setOpenConnection(false);
    setHealthMessage('Integracion guardada. Ya puede usarse desde Canales, Agentes y Workflow Studio.');
    await load();
  };

  const checkConnectionHealth = async (connectionId: string) => {
    try {
      const res = await axios.get(endpoints.agentflow.connections.health(tenantId, connectionId));
      const checks = (res.data?.checks ?? [])
        .map((check: any) => `${check.check}: ${check.status === 'Healthy' ? 'listo' : check.status}`)
        .join(', ');
      setHealthMessage(`Estado ${res.data?.status ?? 'desconocido'} - ${checks || 'sin checks'}`);
      setError(null);
    } catch (err: any) {
      setHealthMessage(null);
      setError(err?.message || 'No se pudo validar la integracion.');
    }
  };

  const install = async (extensionId: string) => {
    await axios.post(`/api/v1/extensions/tenants/${tenantId}/install`, { extensionId, enableAfterInstall: true });
    await load();
  };

  const update = async (entry: Entry) => {
    const content = `${entry.extensionId}|${entry.version}|{}|demo`;
    const encoder = new TextEncoder();
    const hash = await crypto.subtle.digest('SHA-256', encoder.encode(content));
    const signature = Array.from(new Uint8Array(hash)).map((b) => b.toString(16).padStart(2, '0')).join('').toUpperCase();

    await axios.post('/api/v1/extensions/catalog/register', {
      extensionId: entry.extensionId,
      name: entry.name,
      version: entry.version,
      vendor: entry.metadata.vendor,
      description: entry.description,
      permissions: entry.metadata.permissions,
      riskLevel: entry.metadata.riskLevel,
      compatibility: entry.metadata.compatibility,
      source: 'remote-marketplace',
      signatureAlgorithm: 'SHA256',
      signature,
      manifestJson: '{}',
      payloadHash: 'demo',
    });
    await load();
  };

  return (
    <>
      <Helmet>
        <title>Marketplace de conectores | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="xl">
        <Stack spacing={2.5} sx={{ mb: 3 }}>
          <Paper
            variant="outlined"
            sx={{
              p: { xs: 2.5, md: 3 },
              borderRadius: 4,
              background:
                'radial-gradient(circle at 8% 18%, rgba(14,124,90,0.14), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
            }}
          >
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ md: 'center' }}>
              <Stack direction="row" spacing={1.5} alignItems="center">
                <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.lighter', color: 'primary.main' }}>
                  <Iconify icon="mdi:storefront-outline" width={30} />
                </Avatar>
                <Box>
                  <Typography variant="overline" color="text.secondary">
                    Integraciones
                  </Typography>
                  <Typography variant="h3">Marketplace de conectores</Typography>
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                    Instala y configura capacidades para workflows, agentes, canales y herramientas externas.
                  </Typography>
                </Box>
              </Stack>
              <Button variant="contained" href={paths.dashboard.workflows}>
                Usar en Workflow Studio
              </Button>
            </Stack>
          </Paper>

          <Grid container spacing={2}>
            {[
              ['Disponibles', visibleEntries.length, 'mdi:storefront-outline'],
              ['Instalados', installedCount, 'mdi:check-decagram-outline'],
              ['Conexiones', connections.length, 'mdi:connection'],
              ['Listas para usar', readyResources, 'mdi:check-circle-outline'],
              ['En revision', entries.filter((e) => e.metadata.isQuarantined).length, 'mdi:shield-alert-outline'],
            ].map(([label, value, icon]) => (
              <Grid item xs={12} md={3} key={label}>
                <Card variant="outlined" sx={{ p: 2 }}>
                  <Stack direction="row" spacing={1.5} alignItems="center">
                    <Iconify icon={String(icon)} width={30} sx={{ color: 'primary.main' }} />
                    <Box>
                      <Typography variant="h5">{String(value)}</Typography>
                      <Typography variant="caption" color="text.secondary">{label}</Typography>
                    </Box>
                  </Stack>
                </Card>
              </Grid>
            ))}
          </Grid>

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <TextField
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Buscar por nombre, vendor o capacidad"
              size="small"
              sx={{ maxWidth: 420 }}
              fullWidth
            />
            <Button variant="outlined" onClick={load}>Actualizar</Button>
          </Stack>
        </Stack>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        {healthMessage && (
          <Alert severity="info" sx={{ mb: 2 }} onClose={() => setHealthMessage(null)}>
            {healthMessage}
          </Alert>
        )}

        <Card variant="outlined" sx={{ p: 2, mb: 3 }}>
          <Stack spacing={1.5}>
            <Box>
              <Typography variant="h6">Integraciones listas para Workflow Studio</Typography>
              <Typography variant="body2" color="text.secondary">
                Configura una vez y reutiliza en canales, nodos, agentes y workflows.
              </Typography>
            </Box>
            <Grid container spacing={2}>
              {QUICK_CONNECTIONS.map((preset) => {
                const configured = configuredConnectionIds.has(preset.connectorId);
                const configuredConnection = configuredByConnectorId(preset.connectorId);
                return (
                  <Grid item xs={12} md={3} key={preset.id}>
                    <Card variant="outlined" sx={{ height: '100%' }}>
                      <CardContent>
                        <Stack spacing={1.2}>
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Iconify icon={preset.icon} width={26} sx={{ color: 'primary.main' }} />
                            <Box>
                              <Typography variant="subtitle2">{preset.title}</Typography>
                              <Chip
                                size="small"
                                color={configured ? 'success' : 'warning'}
                                label={configured ? 'Configurado' : 'Pendiente'}
                              />
                            </Box>
                          </Stack>
                          <Typography variant="caption" color="text.secondary">
                            {preset.description}
                          </Typography>
                          <Stack direction="row" spacing={0.5} flexWrap="wrap">
                            {preset.capabilities.map((capability) => (
                              <Chip key={capability} size="small" label={CAPABILITY_LABELS[capability] ?? capability} />
                            ))}
                          </Stack>
                          <Stack direction="row" spacing={0.8}>
                            <Button size="small" variant={configured ? 'outlined' : 'contained'} onClick={() => openQuickConnection(preset)}>
                              {configured ? 'Editar' : 'Configurar'}
                            </Button>
                            <Button size="small" variant="outlined" href={paths.dashboard.workflows}>
                              Usar en workflow
                            </Button>
                            {configuredConnection && (
                              <Button size="small" variant="text" onClick={() => checkConnectionHealth(configuredConnection.id)}>
                                Probar
                              </Button>
                            )}
                          </Stack>
                        </Stack>
                      </CardContent>
                    </Card>
                  </Grid>
                );
              })}
            </Grid>
            {connections.length > 0 && (
              <Stack direction="row" spacing={0.8} flexWrap="wrap">
                {connections.map((connection) => (
                  <Chip
                    key={connection.id}
                    color={connection.secretVersion ? 'success' : 'default'}
                    label={`${connection.name}: ${connection.secretVersion ? 'secreto listo' : 'sin secreto'} / ${connection.connectorId}`}
                    onClick={() => checkConnectionHealth(connection.id)}
                  />
                ))}
              </Stack>
            )}
          </Stack>
        </Card>

        <Card variant="outlined" sx={{ p: 2, mb: 3 }}>
          <Stack spacing={1.5}>
            <Box>
              <Typography variant="h6">Recursos disponibles para agentes y workflows</Typography>
              <Typography variant="body2" color="text.secondary">
                Esta es la lista real que consume Workflow Studio. Si algo aparece pendiente, el nodo lo avisara antes de publicar.
              </Typography>
            </Box>
            {resources.length === 0 ? (
              <Alert severity="info">Aun no hay recursos. Configura Twilio, Storage, API o MCP arriba.</Alert>
            ) : (
              <Grid container spacing={2}>
                {resources.map((resource) => (
                  <Grid item xs={12} md={4} key={resource.id}>
                    <Card variant="outlined" sx={{ height: '100%' }}>
                      <CardContent>
                        <Stack spacing={1.2}>
                          <Stack direction="row" justifyContent="space-between" alignItems="center">
                            <Box>
                              <Typography variant="subtitle2">{resource.name}</Typography>
                              <Typography variant="caption" color="text.secondary">
                                {resource.connectorId} / {resource.type}
                              </Typography>
                            </Box>
                            <Chip
                              size="small"
                              color={resource.ready ? 'success' : 'warning'}
                              label={resource.ready ? 'Listo' : 'Pendiente'}
                            />
                          </Stack>
                          <Stack direction="row" spacing={0.5} flexWrap="wrap">
                            {resource.capabilities.map((capability) => (
                              <Chip
                                key={capability}
                                size="small"
                                label={CAPABILITY_LABELS[capability] ?? capability}
                              />
                            ))}
                          </Stack>
                          <Typography variant="caption" color="text.secondary">
                            Nodos sugeridos: {resource.suggestedNodes.map((node) => NODE_LABELS[node] ?? node).join(', ') || 'sin nodos'}
                          </Typography>
                          {!resource.ready && (
                            <Typography variant="caption" color="warning.main">
                              Pendiente: {resource.checks.filter((check) => check.status !== 'Healthy').map((check) => check.check).join(', ')}
                            </Typography>
                          )}
                          <Stack direction="row" spacing={1}>
                            <Button size="small" variant="outlined" onClick={() => openQuickConnection(QUICK_CONNECTIONS.find((preset) => preset.connectorId === resource.connectorId) ?? QUICK_CONNECTIONS[0])}>
                              Editar
                            </Button>
                            <Button size="small" href={paths.dashboard.workflows}>
                              Usar
                            </Button>
                          </Stack>
                        </Stack>
                      </CardContent>
                    </Card>
                  </Grid>
                ))}
              </Grid>
            )}
          </Stack>
        </Card>

        <Grid container spacing={2}>
          {visibleEntries.length === 0 && (
            <Grid item xs={12}>
              <Card variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
                <Iconify icon="mdi:store-search-outline" width={44} sx={{ color: 'text.disabled', mb: 1 }} />
                <Typography variant="subtitle1">No hay conectores para mostrar</Typography>
                <Typography variant="body2" color="text.secondary">
                  Cambia la busqueda o actualiza el catalogo.
                </Typography>
              </Card>
            </Grid>
          )}
          {visibleEntries.map((entry) => {
            const isInstalled = !!installed[entry.extensionId];
            return (
            <Grid item xs={12} md={6} key={entry.extensionId}>
              <Card variant="outlined" sx={{ height: '100%' }}>
                <CardContent>
                  <Stack spacing={1.5}>
                    <Stack direction="row" justifyContent="space-between" alignItems="center">
                      <Stack direction="row" spacing={1.2} alignItems="center">
                        <Box
                          sx={{
                            width: 42,
                            height: 42,
                            borderRadius: 1.5,
                            display: 'grid',
                            placeItems: 'center',
                            bgcolor: 'primary.lighter',
                            color: 'primary.main',
                          }}
                        >
                          <Iconify icon="mdi:puzzle-outline" width={24} />
                        </Box>
                        <Box>
                          <Typography variant="h6">{entry.name}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {entry.metadata.vendor} · v{entry.version}
                          </Typography>
                        </Box>
                      </Stack>
                      <Chip
                        label={isInstalled ? 'Instalado' : 'Disponible'}
                        size="small"
                        color={isInstalled ? 'success' : 'default'}
                        variant="soft"
                      />
                    </Stack>
                    <Typography variant="body2" color="text.secondary">{entry.description}</Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Chip label={`Riesgo: ${entry.metadata.riskLevel}`} size="small" color="warning" />
                      <Chip label={entry.metadata.signatureValid ? 'Firmado' : 'No firmado'} size="small" color={entry.metadata.signatureValid ? 'success' : 'default'} />
                      {entry.metadata.isQuarantined && <Chip label="Cuarentena" color="error" size="small" />}
                    </Stack>
                    <Typography variant="caption">Compatibilidad: {entry.metadata.compatibility}</Typography>
                    <Typography variant="caption">
                      Capacidades: {(entry.metadata.permissions || []).join(', ') || 'sin permisos requeridos'}
                    </Typography>
                    <Stack direction="row" spacing={1}>
                      <Button variant="contained" disabled={isInstalled || entry.metadata.isQuarantined} onClick={() => install(entry.extensionId)}>
                        {isInstalled ? 'Instalado' : 'Instalar'}
                      </Button>
                      <Button variant="outlined" href={paths.dashboard.workflows}>Usar en workflow</Button>
                      <Button variant="text" onClick={() => update(entry)}>Actualizar metadata</Button>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          );})}
        </Grid>
      </DashboardContent>

      <Dialog open={openConnection} onClose={() => setOpenConnection(false)} fullWidth maxWidth="sm">
        <DialogTitle>Configurar integracion</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <TextField
              label="ID interno"
              value={connectionForm.id}
              onChange={(e) => setConnectionForm((prev) => ({ ...prev, id: e.target.value }))}
              fullWidth
            />
            <TextField
              label="Nombre visible"
              value={connectionForm.name}
              onChange={(e) => setConnectionForm((prev) => ({ ...prev, name: e.target.value }))}
              fullWidth
            />
            <TextField
              select
              label="Tipo"
              value={connectionForm.type}
              onChange={(e) => setConnectionForm((prev) => ({ ...prev, type: e.target.value }))}
              fullWidth
            >
              {['Messaging', 'Rest', 'Storage', 'Mcp', 'Sheets'].map((type) => (
                <MenuItem key={type} value={type}>
                  {type}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Connector ID"
              value={connectionForm.connectorId}
              onChange={(e) => setConnectionForm((prev) => ({ ...prev, connectorId: e.target.value }))}
              fullWidth
            />
            {renderGuidedConnectionFields()}
            <TextField
              label="Configuracion avanzada JSON"
              value={connectionForm.configJson}
              onChange={(e) => setConnectionForm((prev) => ({ ...prev, configJson: e.target.value }))}
              fullWidth
              multiline
              minRows={7}
              helperText="Opcional avanzado. Los campos guiados de arriba actualizan este JSON automaticamente."
            />
            <TextField
              label="Secret avanzado JSON o token"
              value={connectionForm.secret}
              onChange={(e) => setConnectionForm((prev) => ({ ...prev, secret: e.target.value }))}
              fullWidth
              multiline
              minRows={3}
              helperText='Se guarda protegido en backend. Para Twilio usa {"authToken":"..."}.'
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenConnection(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveConnection}>
            Guardar integracion
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
