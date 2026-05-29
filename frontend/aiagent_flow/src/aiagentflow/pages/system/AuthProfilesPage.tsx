import { Helmet } from 'react-helmet-async';
import { useSearchParams } from 'react-router';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Select from '@mui/material/Select';
import Dialog from '@mui/material/Dialog';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TextField from '@mui/material/TextField';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';
import { useRouter } from 'src/routes/hooks';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

interface AuthProfile {
  id: string;
  tenantId: string;
  provider: string;
  profileId: string;
  authType: string;
  secretMasked?: string;
  createdAt: string;
  expiresAt?: string;
}

interface ModelItem {
  modelId: string;
  providerId: string;
  displayName: string;
  providerProfileId?: string;
}

const providerOptions = ['OpenAI', 'Anthropic', 'Gemini', 'OpenRouter', 'Groq', 'Deepgram', '11Labs'];

export default function AuthProfilesPage() {
  const router = useRouter();
  const tenantId = useTenantId();
  const [searchParams] = useSearchParams();
  const [profiles, setProfiles] = useState<AuthProfile[]>([]);
  const [models, setModels] = useState<ModelItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [openCreate, setOpenCreate] = useState(false);

  const [form, setForm] = useState({
    provider: 'OpenAI',
    profileId: '',
    authType: 'api_key',
    secret: '',
  });

  const [bind, setBind] = useState({ modelId: '', providerProfileId: '' });

  const fetchAll = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [profilesRes, modelsRes] = await Promise.all([
        axios.get(`/api/v1/tenants/${tenantId}/auth-profiles`),
        axios.get('/api/v1/model-routing/models'),
      ]);

      setProfiles((profilesRes.data ?? []) as AuthProfile[]);
      setModels((modelsRes.data ?? []) as ModelItem[]);
    } catch (err: any) {
      setError(err?.message || 'No se pudieron cargar los perfiles de autenticacion.');
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    void fetchAll();
  }, [fetchAll]);

  const filteredProfiles = useMemo(() => {
    const selectedModel = models.find((model) => model.modelId === bind.modelId);
    if (!selectedModel) return profiles;
    return profiles.filter((profile) => profile.provider === selectedModel.providerId);
  }, [bind.modelId, models, profiles]);

  useEffect(() => {
    const bindModel = searchParams.get('bindModel');
    if (!bindModel || models.length === 0) return;

    const selectedModel = models.find((model) => model.modelId === bindModel);
    if (!selectedModel) return;

    setBind({
      modelId: selectedModel.modelId,
      providerProfileId: selectedModel.providerProfileId || '',
    });
  }, [models, searchParams]);

  const handleCreate = async () => {
    if (!form.profileId.trim()) return;

    try {
      setSaving(true);
      await axios.post(`/api/v1/tenants/${tenantId}/auth-profiles`, {
        provider: form.provider,
        profileId: form.profileId.trim(),
        authType: form.authType,
        secret: form.secret,
      });
      setOpenCreate(false);
      setForm({ provider: 'OpenAI', profileId: '', authType: 'api_key', secret: '' });
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'No se pudo crear el perfil.');
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async (profileId: string) => {
    try {
      const res = await axios.post(`/api/v1/tenants/${tenantId}/auth-profiles/${profileId}/test`);
      alert(`${profileId}: ${res.data?.healthy ? 'Healthy' : 'Unhealthy'} (${res.data?.reason ?? 'n/a'})`);
    } catch (err: any) {
      alert(err?.message || 'La prueba del perfil fallo.');
    }
  };

  const handleDelete = async (profileId: string) => {
    if (!confirm(`Eliminar perfil '${profileId}'?`)) return;

    try {
      await axios.delete(`/api/v1/tenants/${tenantId}/auth-profiles/${profileId}`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'No se pudo eliminar el perfil.');
    }
  };

  const handleBind = async () => {
    if (!bind.modelId || !bind.providerProfileId) return;

    try {
      await axios.post(`/api/v1/model-routing/models/${bind.modelId}/bind-profile`, {
        providerProfileId: bind.providerProfileId,
      });
      await fetchAll();
      alert('Modelo vinculado al perfil correctamente.');
    } catch (err: any) {
      alert(err?.message || 'No se pudo vincular el perfil al modelo.');
    }
  };

  return (
    <>
      <Helmet>
        <title>Provider Auth Profiles | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Provider Auth Profiles</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
              Crea y prueba credenciales por proveedor, luego vincúlalas a los modelos del catálogo.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" onClick={() => router.push(paths.dashboard.system.models)}>
              Ir a Modelos
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={() => setOpenCreate(true)}>
              Agregar Provider Auth Profile
            </Button>
          </Stack>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={7}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Perfiles</Typography>
              {loading ? (
                <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
              ) : profiles.length === 0 ? (
                <Alert severity="info">Aún no hay perfiles de proveedor.</Alert>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Perfil</TableCell>
                      <TableCell>Proveedor</TableCell>
                      <TableCell>Tipo</TableCell>
                      <TableCell>Secret</TableCell>
                      <TableCell align="right">Acciones</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {profiles.map((profile) => (
                      <TableRow key={profile.id} hover>
                        <TableCell>{profile.profileId}</TableCell>
                        <TableCell><Chip label={profile.provider} size="small" variant="outlined" /></TableCell>
                        <TableCell>{profile.authType}</TableCell>
                        <TableCell>{profile.secretMasked ?? '—'}</TableCell>
                        <TableCell align="right">
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            <Button size="small" variant="outlined" onClick={() => handleTest(profile.profileId)}>Test</Button>
                            <Button size="small" color="error" variant="outlined" onClick={() => handleDelete(profile.profileId)}>Delete</Button>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Card>
          </Grid>

          <Grid item xs={12} md={5}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Vincular perfil a modelo</Typography>
              <Stack spacing={2}>
                {!!bind.modelId && !models.find((model) => model.modelId === bind.modelId)?.providerProfileId && (
                  <Alert severity="warning">
                    El modelo seleccionado aún no tiene un perfil de proveedor vinculado.
                  </Alert>
                )}
                <Select
                  value={bind.modelId}
                  displayEmpty
                  onChange={(e) =>
                    setBind({
                      modelId: String(e.target.value),
                      providerProfileId: '',
                    })
                  }
                >
                  <MenuItem value=""><em>Seleccionar modelo</em></MenuItem>
                  {models.map((model) => (
                    <MenuItem key={model.modelId} value={model.modelId}>
                      {model.displayName} ({model.providerId})
                    </MenuItem>
                  ))}
                </Select>

                <Select
                  value={bind.providerProfileId}
                  displayEmpty
                  onChange={(e) => setBind((prev) => ({ ...prev, providerProfileId: String(e.target.value) }))}
                >
                  <MenuItem value=""><em>Seleccionar perfil</em></MenuItem>
                  {filteredProfiles.map((profile) => (
                    <MenuItem key={profile.id} value={profile.profileId}>
                      {profile.profileId} ({profile.provider})
                    </MenuItem>
                  ))}
                </Select>

                <Button variant="contained" onClick={handleBind} disabled={!bind.modelId || !bind.providerProfileId}>
                  Vincular
                </Button>

                <Divider />

                <Typography variant="subtitle2">Vínculos actuales</Typography>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Modelo</TableCell>
                      <TableCell>Proveedor</TableCell>
                      <TableCell>Perfil vinculado</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {models.map((model) => (
                      <TableRow key={model.modelId}>
                        <TableCell>{model.displayName}</TableCell>
                        <TableCell>{model.providerId}</TableCell>
                        <TableCell>{model.providerProfileId || '—'}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Stack>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      <Dialog open={openCreate} onClose={() => setOpenCreate(false)} fullWidth maxWidth="sm">
        <DialogTitle>Crear Provider Auth Profile</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Select value={form.provider} onChange={(e) => setForm((prev) => ({ ...prev, provider: String(e.target.value) }))}>
              {providerOptions.map((provider) => (
                <MenuItem key={provider} value={provider}>{provider}</MenuItem>
              ))}
            </Select>
            <TextField
              label="Profile ID"
              value={form.profileId}
              onChange={(e) => setForm((prev) => ({ ...prev, profileId: e.target.value }))}
              placeholder="openai-personal"
            />
            <TextField
              label="Auth Type"
              value={form.authType}
              onChange={(e) => setForm((prev) => ({ ...prev, authType: e.target.value }))}
            />
            <TextField
              label="Secret / API Key"
              type="password"
              value={form.secret}
              onChange={(e) => setForm((prev) => ({ ...prev, secret: e.target.value }))}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenCreate(false)}>Cancelar</Button>
          <Button variant="contained" onClick={handleCreate} disabled={saving || !form.profileId || !form.secret}>
            {saving ? 'Guardando...' : 'Guardar'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
