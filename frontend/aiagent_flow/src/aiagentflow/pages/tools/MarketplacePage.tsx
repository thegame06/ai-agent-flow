import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
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

export default function MarketplacePage() {
  const tenantId = useTenantId();
  const [query, setQuery] = useState('');
  const [entries, setEntries] = useState<Entry[]>([]);
  const [installed, setInstalled] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [catalogRes, statesRes] = await Promise.all([
        axios.get('/api/v1/extensions/catalog', { params: query ? { q: query } : {} }),
        axios.get(`/api/v1/extensions/tenants/${tenantId}/states`),
      ]);

      setEntries(catalogRes.data ?? []);
      setInstalled(statesRes.data ?? {});
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
          <Box>
            <Typography variant="h4">Marketplace de conectores</Typography>
            <Typography variant="body2" color="text.secondary">
              Instala capacidades para workflows, agentes, canales y herramientas externas.
            </Typography>
          </Box>

          <Grid container spacing={2}>
            {[
              ['Disponibles', visibleEntries.length, 'mdi:storefront-outline'],
              ['Instalados', installedCount, 'mdi:check-decagram-outline'],
              ['En revision', entries.filter((e) => e.metadata.isQuarantined).length, 'mdi:shield-alert-outline'],
            ].map(([label, value, icon]) => (
              <Grid item xs={12} md={4} key={label}>
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
                      <Button variant="outlined" onClick={() => update(entry)}>Actualizar metadata</Button>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          );})}
        </Grid>
      </DashboardContent>
    </>
  );
}
