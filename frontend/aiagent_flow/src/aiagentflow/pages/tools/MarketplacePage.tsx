import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

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

  const load = async () => {
    setError(null);
    try {
      const [catalogRes, statesRes] = await Promise.all([
        axios.get('/api/v1/extensions/catalog', { params: query ? { q: query } : {} }),
        axios.get(`/api/v1/extensions/tenants/${tenantId}/states`),
      ]);

      setEntries(catalogRes.data ?? []);
      setInstalled(statesRes.data ?? {});
    } catch (err: any) {
      setError(err?.message || 'Failed to load marketplace');
    }
  };

  useEffect(() => {
    load();
  }, [tenantId]);

  const visibleEntries = useMemo(
    () => entries.filter((e) => !query || `${e.name} ${e.extensionId}`.toLowerCase().includes(query.toLowerCase())),
    [entries, query]
  );

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
        <title>Extension Marketplace | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="xl">
        <Stack spacing={2} sx={{ mb: 3 }}>
          <Typography variant="h4">Extension Marketplace</Typography>
          <Typography variant="body2" color="text.secondary">
            Browse, search, install and update plugins by tenant.
          </Typography>
          <Stack direction="row" spacing={1}>
            <TextField value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search plugin/vendor" size="small" />
            <Button variant="outlined" onClick={load}>Refresh</Button>
          </Stack>
        </Stack>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={2}>
          {visibleEntries.map((entry) => (
            <Grid item xs={12} md={6} key={entry.extensionId}>
              <Card>
                <CardContent>
                  <Stack spacing={1.5}>
                    <Stack direction="row" justifyContent="space-between" alignItems="center">
                      <Typography variant="h6">{entry.name}</Typography>
                      <Chip label={`v${entry.version}`} size="small" />
                    </Stack>
                    <Typography variant="body2" color="text.secondary">{entry.description}</Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Chip label={entry.metadata.vendor} size="small" />
                      <Chip label={entry.metadata.riskLevel} size="small" color="warning" />
                      <Chip label={entry.metadata.signatureValid ? 'Signed' : 'Unsigned'} size="small" color={entry.metadata.signatureValid ? 'success' : 'default'} />
                      {entry.metadata.isQuarantined && <Chip label="Quarantine" color="error" size="small" />}
                    </Stack>
                    <Typography variant="caption">Compatibility: {entry.metadata.compatibility}</Typography>
                    <Typography variant="caption">Permissions: {(entry.metadata.permissions || []).join(', ') || 'none'}</Typography>
                    <Stack direction="row" spacing={1}>
                      <Button variant="contained" disabled={!!installed[entry.extensionId] || entry.metadata.isQuarantined} onClick={() => install(entry.extensionId)}>Install</Button>
                      <Button variant="outlined" onClick={() => update(entry)}>Update metadata</Button>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>
      </DashboardContent>
    </>
  );
}
