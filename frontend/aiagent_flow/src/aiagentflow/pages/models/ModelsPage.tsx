import { Helmet } from 'react-helmet-async';
import { usePopover } from 'minimal-shared/hooks';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import CardContent from '@mui/material/CardContent';
import CardActions from '@mui/material/CardActions';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { useRouter } from 'src/routes/hooks';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';
import { CustomPopover } from 'src/components/custom-popover';

import { AddModelDialog } from './components/AddModelDialog';

type ModelItem = {
  modelId: string;
  providerId: string;
  displayName: string;
  costPer1KTokens: number;
  maxContextTokens: number;
  tier: string;
  status: string;
  providerProfileId?: string;
};

const tierColor = (tier: string) => {
  switch (tier) {
    case 'Primary':
      return 'success';
    case 'Fallback':
      return 'warning';
    case 'Secondary':
      return 'info';
    default:
      return 'default';
  }
};

export default function ModelsPage() {
  const theme = useTheme();
  const router = useRouter();
  const tenantId = useTenantId();
  const [models, setModels] = useState<ModelItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [addModelOpen, setAddModelOpen] = useState(false);
  const [editingModel, setEditingModel] = useState<ModelItem | null>(null);
  const [providerFilter, setProviderFilter] = useState('all');
  const [error, setError] = useState<string | null>(null);

  const fetchModels = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await axios.get('/api/v1/model-routing/models');
      setModels((response.data ?? []) as ModelItem[]);
    } catch (err: any) {
      const message =
        err?.status === 403
          ? 'El catalogo de modelos requiere permisos de administrador de plataforma.'
          : 'No se pudo cargar el catalogo de modelos.';
      setError(message);
      setModels([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void fetchModels();
  }, []);

  const providerOptions = useMemo(
    () => ['all', ...new Set(models.map((model) => model.providerId).filter(Boolean))],
    [models]
  );

  const filteredModels = useMemo(
    () =>
      models.filter((model) =>
        providerFilter === 'all' ? true : model.providerId === providerFilter
      ),
    [models, providerFilter]
  );

  const handleConfigure = (modelId: string) => {
    const selectedModel = models.find((model) => model.modelId === modelId) ?? null;
    setEditingModel(selectedModel);
    setAddModelOpen(true);
  };

  const handleSetPrimary = async (modelId: string) => {
    try {
      await axios.post(`/api/v1/model-routing/models/${modelId}/set-primary`);
      await fetchModels();
    } catch (err: any) {
      alert(err?.message || 'No se pudo marcar el modelo como primario.');
    }
  };

  const handleTestConnection = async (modelId: string) => {
    try {
      const response = await axios.post(`/api/v1/model-routing/models/${modelId}/test`);
      const healthy = response.data?.healthy ?? response.data?.Healthy;
      alert(healthy ? `El modelo '${modelId}' esta saludable.` : `El modelo '${modelId}' no esta saludable.`);
    } catch (err: any) {
      alert(err?.message || 'No se pudo probar el modelo.');
    }
  };

  const handleDisable = async (modelId: string) => {
    try {
      await axios.delete(`/api/v1/model-routing/models/${modelId}`);
      await fetchModels();
    } catch (err: any) {
      alert(err?.message || 'No se pudo deshabilitar el modelo.');
    }
  };

  return (
    <>
      <Helmet>
        <title>Modelos | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Box>
              <Typography variant="h4">Modelos y enrutamiento</Typography>
              <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
                Administra modelos, proveedores, prioridades y perfiles de autenticacion vinculados.
              </Typography>
            </Box>

            <Stack direction="row" spacing={1}>
              <Button variant="outlined" onClick={() => router.push(paths.dashboard.system.authProfiles)}>
                Provider Auth Profiles
              </Button>
              <Button
                variant="contained"
                startIcon={<Iconify icon="mingcute:add-line" />}
                onClick={() => {
                  setEditingModel(null);
                  setAddModelOpen(true);
                }}
              >
                Agregar modelo
              </Button>
            </Stack>
          </Box>

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 2 }}>
            <TextField
              select
              label="Proveedor"
              value={providerFilter}
              onChange={(e) => setProviderFilter(e.target.value)}
              sx={{ minWidth: 220 }}
            >
              <MenuItem value="all">Todos</MenuItem>
              {providerOptions
                .filter((provider) => provider !== 'all')
                .map((provider) => (
                  <MenuItem key={provider} value={provider}>
                    {provider}
                  </MenuItem>
                ))}
            </TextField>
            <Chip
              label={`${filteredModels.length} modelo${filteredModels.length === 1 ? '' : 's'}`}
              variant="outlined"
              sx={{ width: 'fit-content' }}
            />
          </Stack>
        </Box>

        {error && (
          <Alert severity="warning" sx={{ mb: 3 }}>
            {error}
          </Alert>
        )}

        {loading ? (
          <LinearProgress />
        ) : filteredModels.length === 0 ? (
          <Alert severity="info">No hay modelos para el filtro seleccionado.</Alert>
        ) : (
          <Grid container spacing={3}>
            {filteredModels.map((model) => (
              <Grid key={model.modelId} item xs={12} sm={6} md={4}>
                <Card
                  sx={{
                    height: '100%',
                    display: 'flex',
                    flexDirection: 'column',
                    border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
                    transition: 'all 0.3s ease',
                    '&:hover': {
                      boxShadow: theme.shadows[12],
                      transform: 'translateY(-4px)',
                    },
                  }}
                >
                  <CardContent sx={{ flexGrow: 1 }}>
                    <Stack spacing={2}>
                      <Box
                        sx={{
                          display: 'flex',
                          justifyContent: 'space-between',
                          alignItems: 'flex-start',
                        }}
                      >
                        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                          <Typography variant="h6" noWrap>
                            {model.displayName}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {model.providerId} · {model.modelId}
                          </Typography>
                        </Box>
                        <ModelMenu
                          modelId={model.modelId}
                          onConfigure={handleConfigure}
                          onSetPrimary={handleSetPrimary}
                          onDisable={handleDisable}
                        />
                      </Box>

                        <Stack direction="row" spacing={1} alignItems="center">
                          <Label color={model.status === 'Active' ? 'success' : 'default'}>
                            {model.status}
                          </Label>
                        <Chip
                          label={model.tier}
                          size="small"
                          color={tierColor(model.tier)}
                          variant="soft"
                          />
                          <Chip label={model.providerId} size="small" variant="outlined" />
                          {!model.providerProfileId && (
                            <Chip
                              label="Sin credencial"
                              size="small"
                              color="warning"
                              variant="soft"
                            />
                          )}
                        </Stack>

                      <Divider />

                      <Stack spacing={1}>
                        <MetaRow
                          icon="mdi:server-network-outline"
                          label="Proveedor"
                          value={model.providerId}
                        />
                        <MetaRow icon="mdi:key-chain-variant" label="Perfil vinculado" value={model.providerProfileId || 'Sin vincular'} />
                        <MetaRow icon="mdi:identifier" label="Model ID" value={model.modelId} noWrap />
                        <MetaRow icon="mdi:currency-usd" label="Costo/1K tokens" value={`$${model.costPer1KTokens}`} />
                        <MetaRow
                          icon="mdi:text-box-outline"
                          label="Contexto maximo"
                          value={model.maxContextTokens > 0 ? `${(model.maxContextTokens / 1000).toFixed(0)}K` : 'N/A'}
                        />
                      </Stack>

                      <Divider />

                      <Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                          <Typography variant="caption" color="text.secondary">
                            Salud estimada
                          </Typography>
                          <Typography variant="caption" fontWeight={600}>
                            99.9%
                          </Typography>
                        </Box>
                        <LinearProgress
                          variant="determinate"
                          value={99.9}
                          color="primary"
                          sx={{ height: 6, borderRadius: 1 }}
                        />
                      </Box>
                    </Stack>
                  </CardContent>

                  <CardActions sx={{ px: 2, pb: 2 }}>
                    <Button
                      variant="outlined"
                      startIcon={<Iconify icon="solar:pen-outline" />}
                      onClick={() => handleConfigure(model.modelId)}
                    >
                      Editar
                    </Button>
                    <Button
                      variant={model.providerProfileId ? 'outlined' : 'soft'}
                      color={model.providerProfileId ? 'inherit' : 'warning'}
                      startIcon={<Iconify icon="mdi:key-chain-variant" />}
                      onClick={() =>
                        router.push(
                          `${paths.dashboard.system.authProfiles}?bindModel=${encodeURIComponent(model.modelId)}`
                        )
                      }
                    >
                      Perfil
                    </Button>
                    <Button
                      variant="contained"
                      startIcon={<Iconify icon="mdi:connection" />}
                      onClick={() => handleTestConnection(model.modelId)}
                    >
                      Probar
                    </Button>
                  </CardActions>
                </Card>
              </Grid>
            ))}
          </Grid>
        )}
      </DashboardContent>

      <AddModelDialog
        open={addModelOpen}
        onClose={() => {
          setAddModelOpen(false);
          setEditingModel(null);
        }}
        onSuccess={() => {
          setAddModelOpen(false);
          setEditingModel(null);
          void fetchModels();
        }}
        tenantId={tenantId}
        initialModel={editingModel}
      />
    </>
  );
}

function MetaRow({
  icon,
  label,
  value,
  noWrap = false,
}: {
  icon: string;
  label: string;
  value: string;
  noWrap?: boolean;
}) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}>
      <Typography variant="caption" color="text.secondary">
        <Iconify icon={icon} width={14} sx={{ mr: 0.5, verticalAlign: 'text-bottom' }} />
        {label}
      </Typography>
      <Typography variant="caption" fontWeight={600} noWrap={noWrap}>
        {value}
      </Typography>
    </Box>
  );
}

interface ModelMenuProps {
  modelId: string;
  onConfigure: (id: string) => void | Promise<void>;
  onSetPrimary: (id: string) => void | Promise<void>;
  onDisable: (id: string) => void | Promise<void>;
}

function ModelMenu({ modelId, onConfigure, onSetPrimary, onDisable }: ModelMenuProps) {
  const { open, anchorEl, onClose, onOpen } = usePopover();

  return (
    <>
      <IconButton onClick={onOpen}>
        <Iconify icon="eva:more-vertical-fill" />
      </IconButton>

      <CustomPopover open={open} anchorEl={anchorEl} onClose={onClose}>
        <MenuItem
          onClick={() => {
            onClose();
            onConfigure(modelId);
          }}
        >
          <Iconify icon="solar:pen-outline" />
          Editar
        </MenuItem>

        <MenuItem
          onClick={() => {
            onClose();
            onSetPrimary(modelId);
          }}
        >
          <Iconify icon="mdi:star-outline" />
          Marcar como primario
        </MenuItem>

        <Divider sx={{ borderStyle: 'dashed' }} />

        <MenuItem
          onClick={() => {
            onClose();
            onDisable(modelId);
          }}
          sx={{ color: 'error.main' }}
        >
          <Iconify icon="mdi:cancel" />
          Deshabilitar
        </MenuItem>
      </CustomPopover>
    </>
  );
}
