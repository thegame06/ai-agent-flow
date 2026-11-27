import { Helmet } from 'react-helmet-async';
import { useSearchParams } from 'react-router';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { useTenantApps } from 'src/aiagentflow/hooks/useTenantApps';

import { Iconify } from 'src/components/iconify';

import { CampaignWizard } from './CampaignWizard';
import { CampaignDetail } from './CampaignDetail';
import { dateTime, parseFilter, CAMPAIGN_TYPE, SCHEDULE_LABEL, CAMPAIGN_STATUS } from './campaignModel';

import type { Campaign } from './campaignModel';
import type { WizardPreset } from './CampaignWizard';

// ----------------------------------------------------------------------

export default function CampaignsPage() {
  const tenantId = useTenantId();
  const apps = useTenantApps();
  const [searchParams, setSearchParams] = useSearchParams();

  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState('');
  const [typeFilter, setTypeFilter] = useState('');
  const [query, setQuery] = useState('');

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [editing, setEditing] = useState<Campaign | null>(null);
  const [preset, setPreset] = useState<WizardPreset | null>(null);

  const collectionsEnabled = apps === null || apps.has('collections');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get<Campaign[]>(endpoints.agentflow.campaigns.list(tenantId));
      setCampaigns(res.data ?? []);
    } catch (err: any) {
      setError(
        err?.response?.data?.errorCode === 'APP_MODULE_DISABLED'
          ? 'La app de Campañas no está habilitada para este cliente.'
          : 'No se pudieron cargar las campañas.'
      );
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  // Deep link from Cobranza: /campaigns?audience=collections&stage=Dpd1To30 opens the wizard pre-filled.
  useEffect(() => {
    const audience = searchParams.get('audience');
    if (audience === 'collections' || audience === 'commerce') {
      setPreset({ source: audience, stage: searchParams.get('stage') ?? undefined });
      setEditing(null);
      setWizardOpen(true);
      setSearchParams({}, { replace: true });
    }
  }, [searchParams, setSearchParams]);

  const filtered = useMemo(
    () =>
      campaigns.filter(
        (c) =>
          (!statusFilter || String(c.status) === statusFilter) &&
          (!typeFilter || String(c.campaignType) === typeFilter) &&
          (!query || c.name.toLowerCase().includes(query.toLowerCase()))
      ),
    [campaigns, statusFilter, typeFilter, query]
  );

  const selected = campaigns.find((c) => c.id === selectedId) ?? null;
  const activeCount = campaigns.filter((c) => c.enabled && (c.status === 1 || c.status === 2)).length;
  const scheduled = campaigns.filter((c) => c.nextRunAt).sort((a, b) => (a.nextRunAt! < b.nextRunAt! ? -1 : 1));

  const openNew = () => {
    setPreset(null);
    setEditing(null);
    setWizardOpen(true);
  };

  return (
    <>
      <Helmet>
        <title>Campañas</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} sx={{ mb: 3 }}>
          <Box>
            <Typography variant="h4">Campañas</Typography>
            <Typography variant="body2" color="text.secondary">
              Envía mensajes a una audiencia y mide qué respondió cada contacto: aceptó, no le interesa, prometió pagar, no se
              pudo contactar…
            </Typography>
          </Box>
          <Button variant="contained" startIcon={<Iconify icon="mdi:plus" />} onClick={openNew} sx={{ alignSelf: 'flex-start' }}>
            Nueva campaña
          </Button>
        </Stack>

        {error && (
          <Alert severity="warning" sx={{ mb: 3 }}>
            {error}
          </Alert>
        )}

        <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(3, 1fr)' }, mb: 3 }}>
          <Stat label="Campañas" value={campaigns.length} />
          <Stat label="Activas" value={activeCount} />
          <Card variant="outlined">
            <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
              <Typography variant="caption" color="text.secondary">
                Próximo envío programado
              </Typography>
              <Typography variant="subtitle1" noWrap>
                {scheduled[0] ? `${dateTime(scheduled[0].nextRunAt)} · ${scheduled[0].name}` : 'Ninguno'}
              </Typography>
            </CardContent>
          </Card>
        </Box>

        <Card>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2 }}>
            <TextField size="small" placeholder="Buscar campaña" value={query} onChange={(e) => setQuery(e.target.value)} sx={{ flex: 1 }} />
            <TextField select size="small" label="Estado" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} sx={{ minWidth: 160 }}>
              <MenuItem value="">Todos</MenuItem>
              {CAMPAIGN_STATUS.map((s, i) => (
                <MenuItem key={s.label} value={String(i)}>
                  {s.label}
                </MenuItem>
              ))}
            </TextField>
            <TextField select size="small" label="Tipo" value={typeFilter} onChange={(e) => setTypeFilter(e.target.value)} sx={{ minWidth: 160 }}>
              <MenuItem value="">Todos</MenuItem>
              {CAMPAIGN_TYPE.map((t, i) => (
                <MenuItem key={t} value={String(i)}>
                  {t}
                </MenuItem>
              ))}
            </TextField>
          </Stack>

          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Campaña</TableCell>
                  <TableCell>Estado</TableCell>
                  <TableCell>Audiencia</TableCell>
                  <TableCell>Envío</TableCell>
                  <TableCell>Programación</TableCell>
                  <TableCell>Último envío</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {loading && (
                  <TableRow>
                    <TableCell colSpan={6} align="center" sx={{ py: 4 }}>
                      <CircularProgress size={24} />
                    </TableCell>
                  </TableRow>
                )}
                {!loading && filtered.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6} align="center" sx={{ py: 6 }}>
                      <Typography variant="subtitle1">Aún no hay campañas</Typography>
                      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                        Crea una para contactar a tu cartera o a tus clientes por WhatsApp.
                      </Typography>
                      <Button variant="outlined" onClick={openNew}>
                        Crear campaña
                      </Button>
                    </TableCell>
                  </TableRow>
                )}
                {!loading &&
                  filtered.map((c) => {
                    const st = CAMPAIGN_STATUS[c.status] ?? CAMPAIGN_STATUS[0];
                    const filter = parseFilter(c.audienceFilterJson);
                    return (
                      <TableRow key={c.id} hover sx={{ cursor: 'pointer' }} onClick={() => setSelectedId(c.id)}>
                        <TableCell>
                          <Typography variant="subtitle2">{c.name}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {CAMPAIGN_TYPE[c.campaignType]}
                          </Typography>
                        </TableCell>
                        <TableCell>
                          <Chip size="small" label={st.label} color={st.color} variant="soft" />
                        </TableCell>
                        <TableCell>
                          {filter.source === 'collections' ? 'Cartera' : 'Clientes'}
                          {filter.stages.length > 0 && (
                            <Typography variant="caption" color="text.secondary" component="div">
                              {filter.stages.length} etapa(s)
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>
                          {c.channel === 'whatsapp' ? 'WhatsApp' : c.channel}
                          <Typography variant="caption" color="text.secondary" component="div">
                            {c.workflowDefinitionId ? 'Automatización' : 'Mensaje directo'}
                          </Typography>
                        </TableCell>
                        <TableCell>
                          {c.triggerType === 1 ? 'Manual' : SCHEDULE_LABEL[c.scheduleType]}
                          {c.nextRunAt && (
                            <Typography variant="caption" color="text.secondary" component="div">
                              próximo {dateTime(c.nextRunAt)}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>{c.lastRunAt ? dateTime(c.lastRunAt) : 'Nunca'}</TableCell>
                      </TableRow>
                    );
                  })}
              </TableBody>
            </Table>
          </TableContainer>
        </Card>
      </DashboardContent>

      <CampaignDetail
        tenantId={tenantId}
        campaign={selected}
        onClose={() => setSelectedId(null)}
        onEdit={(c) => {
          setEditing(c);
          setPreset(null);
          setWizardOpen(true);
        }}
        onChanged={load}
      />

      <CampaignWizard
        open={wizardOpen}
        tenantId={tenantId}
        campaign={editing}
        preset={preset}
        collectionsEnabled={collectionsEnabled}
        onClose={() => setWizardOpen(false)}
        onSaved={(saved) => {
          setWizardOpen(false);
          load();
          setSelectedId(saved.id);
        }}
      />
    </>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <Card variant="outlined">
      <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
        <Typography variant="caption" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5">{value.toLocaleString('es-MX')}</Typography>
      </CardContent>
    </Card>
  );
}
