import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';
import { usePopover } from 'minimal-shared/hooks';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import CardContent from '@mui/material/CardContent';
import CardActions from '@mui/material/CardActions';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { paths } from 'src/routes/paths';
import { useRouter } from 'src/routes/hooks';
import { DashboardContent } from 'src/layouts/dashboard';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';
import { CustomPopover } from 'src/components/custom-popover';

import { AddModelDialog } from './components/AddModelDialog';

// ----------------------------------------------------------------------

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

// ----------------------------------------------------------------------

export default function ModelsPage() {
  const theme = useTheme();
  const router = useRouter();
  const [models, setModels] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [addModelOpen, setAddModelOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchModels = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await axios.get('/api/v1/model-routing/models');
      setModels(response.data);
    } catch (err: any) {
      const message =
        err?.status === 403
          ? 'Model catalog requires platform admin permissions.'
          : 'Unable to load model catalog.';
      setError(message);
      setModels([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchModels();
  }, []);

  const handleConfigure = (modelId: string) => {
    alert(`Advanced model configuration is pending. Selected: ${modelId}`);
  };

  const handleSetPrimary = async (modelId: string) => {
    try {
      await axios.post(`/api/v1/model-routing/models/${modelId}/set-primary`);
      await fetchModels();
    } catch (err: any) {
      alert(err?.message || 'Failed to set model as primary.');
    }
  };

  const handleTestConnection = async (modelId: string) => {
    try {
      const response = await axios.post(`/api/v1/model-routing/models/${modelId}/test`);
      const healthy = response.data?.healthy ?? response.data?.Healthy;
      alert(healthy ? `Model '${modelId}' is healthy.` : `Model '${modelId}' is unhealthy.`);
    } catch (err: any) {
      alert(err?.message || 'Failed to test model connection.');
    }
  };

  const handleDisable = async (modelId: string) => {
    try {
      await axios.delete(`/api/v1/model-routing/models/${modelId}`);
      await fetchModels();
    } catch (err: any) {
      alert(err?.message || 'Failed to disable model.');
    }
  };

  return (
    <>
      <Helmet>
        <title>Model Routing | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Box>
              <Typography variant="h4">Model Routing & Configuration</Typography>
              <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
                Configure LLM providers, routing priorities, fallbacks, and cost controls
              </Typography>
            </Box>

            <Stack direction="row" spacing={1}>
              <Button
                variant="outlined"
                onClick={() => router.push(paths.dashboard.system.authProfiles)}
              >
                Auth Profiles
              </Button>
              <Button
                variant="contained"
                startIcon={<Iconify icon="mingcute:add-line" />}
                onClick={() => setAddModelOpen(true)}
              >
                Add Model
              </Button>
            </Stack>
          </Box>
        </Box>

        {error && (
          <Alert severity="warning" sx={{ mb: 3 }}>
            {error}
          </Alert>
        )}

        {loading ? (
          <LinearProgress />
        ) : models.length === 0 ? (
          <Alert severity="info">
            No models are available in the routing registry for your current role or tenant.
          </Alert>
        ) : (
          <Grid container spacing={3}>
            {models.map((model) => (
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
                      {/* Header */}
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
                        </Box>
                        <ModelMenu
                          modelId={model.modelId}
                          onConfigure={handleConfigure}
                          onSetPrimary={handleSetPrimary}
                          onDisable={handleDisable}
                        />
                      </Box>

                      {/* Status & Tier */}
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
                      </Stack>

                      <Divider />

                      {/* Stats */}
                      <Stack spacing={1}>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="caption" color="text.secondary">
                            <Iconify
                              icon="mdi:identifier"
                              width={14}
                              sx={{ mr: 0.5, verticalAlign: 'text-bottom' }}
                            />
                            Model ID
                          </Typography>
                          <Typography variant="caption" fontWeight={600} noWrap>
                            {model.modelId}
                          </Typography>
                        </Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="caption" color="text.secondary">
                            <Iconify
                              icon="mdi:currency-usd"
                              width={14}
                              sx={{ mr: 0.5, verticalAlign: 'text-bottom' }}
                            />
                            Cost/1K tokens
                          </Typography>
                          <Typography variant="caption" fontWeight={600}>
                            ${model.costPer1KTokens}
                          </Typography>
                        </Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="caption" color="text.secondary">
                            <Iconify
                              icon="mdi:text-box-outline"
                              width={14}
                              sx={{ mr: 0.5, verticalAlign: 'text-bottom' }}
                            />
                            Max Tokens
                          </Typography>
                          <Typography variant="caption" fontWeight={600}>
                            {(model.maxContextTokens / 1000).toFixed(0)}K
                          </Typography>
                        </Box>
                      </Stack>

                      <Divider />

                      {/* Reliability */}
                      <Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                          <Typography variant="caption" color="text.secondary">
                            Reliability
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
                      fullWidth
                      variant="outlined"
                      startIcon={<Iconify icon="mdi:cog-outline" />}
                      onClick={() => handleConfigure(model.modelId)}
                    >
                      Configure
                    </Button>
                    <Button
                      fullWidth
                      variant="contained"
                      startIcon={<Iconify icon="mdi:connection" />}
                      onClick={() => handleTestConnection(model.modelId)}
                    >
                      Test
                    </Button>
                  </CardActions>
                </Card>
              </Grid>
            ))}
          </Grid>
        )}
      </DashboardContent>

      {/* Add Model Dialog */}
      <AddModelDialog
        open={addModelOpen}
        onClose={() => setAddModelOpen(false)}
        onSuccess={() => {
          setAddModelOpen(false);
          fetchModels();
        }}
      />
    </>
  );
}

// ----------------------------------------------------------------------

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
          <Iconify icon="mdi:cog-outline" />
          Configure
        </MenuItem>

        <MenuItem
          onClick={() => {
            onClose();
            onSetPrimary(modelId);
          }}
        >
          <Iconify icon="mdi:star-outline" />
          Set as Primary
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
          Disable
        </MenuItem>
      </CustomPopover>
    </>
  );
}
