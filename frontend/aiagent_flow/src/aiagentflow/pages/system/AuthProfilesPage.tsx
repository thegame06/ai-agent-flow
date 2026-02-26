import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

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

export default function AuthProfilesPage() {
  const router = useRouter();
  const TENANT_ID = useTenantId();
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

  const providerOptions = useMemo(() => ['OpenAI', 'Anthropic', 'Gemini', 'OpenRouter', 'Groq'], []);

  const fetchAll = async () => {
    try {
      setLoading(true);
      setError(null);
      const [profilesRes, modelsRes] = await Promise.all([
        axios.get(`/api/v1/tenants/${TENANT_ID}/auth-profiles`),
        axios.get('/api/v1/model-routing/models'),
      ]);

      setProfiles((profilesRes.data ?? []) as AuthProfile[]);
      setModels((modelsRes.data ?? []) as ModelItem[]);
    } catch (err: any) {
      setError(err?.message || 'Failed to load auth profiles');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchAll();
  }, []);

  const handleCreate = async () => {
    if (!form.profileId.trim()) return;

    try {
      setSaving(true);
      await axios.post(`/api/v1/tenants/${TENANT_ID}/auth-profiles`, {
        provider: form.provider,
        profileId: form.profileId.trim(),
        authType: form.authType,
        secret: form.secret,
      });
      setOpenCreate(false);
      setForm({ provider: 'OpenAI', profileId: '', authType: 'api_key', secret: '' });
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Failed to create profile');
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async (profileId: string) => {
    try {
      const res = await axios.post(`/api/v1/tenants/${TENANT_ID}/auth-profiles/${profileId}/test`);
      alert(`${profileId}: ${res.data?.healthy ? 'Healthy' : 'Unhealthy'} (${res.data?.reason ?? 'n/a'})`);
    } catch (err: any) {
      alert(err?.message || 'Test failed');
    }
  };

  const handleDelete = async (profileId: string) => {
    if (!confirm(`Delete profile '${profileId}'?`)) return;

    try {
      await axios.delete(`/api/v1/tenants/${TENANT_ID}/auth-profiles/${profileId}`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Failed to delete profile');
    }
  };

  const handleBind = async () => {
    if (!bind.modelId || !bind.providerProfileId) return;

    try {
      await axios.post(`/api/v1/model-routing/models/${bind.modelId}/bind-profile`, {
        providerProfileId: bind.providerProfileId,
      });
      await fetchAll();
      alert('Model linked to profile successfully');
    } catch (err: any) {
      alert(err?.message || 'Failed to bind model profile');
    }
  };

  return (
    <>
      <Helmet>
        <title>Auth Profiles | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Provider Auth Profiles</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
              Create and test provider credentials, then bind them to model routing.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" onClick={() => router.push(paths.dashboard.system.models)}>
              Go to Models
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={() => setOpenCreate(true)}>
              Add Auth Profile
            </Button>
          </Stack>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={7}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Profiles</Typography>
              {loading ? (
                <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
              ) : profiles.length === 0 ? (
                <Alert severity="info">No auth profiles yet.</Alert>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Profile</TableCell>
                      <TableCell>Provider</TableCell>
                      <TableCell>Type</TableCell>
                      <TableCell>Secret</TableCell>
                      <TableCell align="right">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {profiles.map((p) => (
                      <TableRow key={p.id} hover>
                        <TableCell>{p.profileId}</TableCell>
                        <TableCell><Chip label={p.provider} size="small" variant="outlined" /></TableCell>
                        <TableCell>{p.authType}</TableCell>
                        <TableCell>{p.secretMasked ?? '—'}</TableCell>
                        <TableCell align="right">
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            <Button size="small" variant="outlined" onClick={() => handleTest(p.profileId)}>Test</Button>
                            <Button size="small" color="error" variant="outlined" onClick={() => handleDelete(p.profileId)}>Delete</Button>
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
              <Typography variant="h6" sx={{ mb: 2 }}>Bind profile to model</Typography>
              <Stack spacing={2}>
                <Select
                  value={bind.modelId}
                  displayEmpty
                  onChange={(e) => setBind((prev) => ({ ...prev, modelId: String(e.target.value) }))}
                >
                  <MenuItem value=""><em>Select model</em></MenuItem>
                  {models.map((m) => (
                    <MenuItem key={m.modelId} value={m.modelId}>
                      {m.displayName} ({m.modelId})
                    </MenuItem>
                  ))}
                </Select>

                <Select
                  value={bind.providerProfileId}
                  displayEmpty
                  onChange={(e) => setBind((prev) => ({ ...prev, providerProfileId: String(e.target.value) }))}
                >
                  <MenuItem value=""><em>Select profile</em></MenuItem>
                  {profiles.map((p) => (
                    <MenuItem key={p.id} value={p.profileId}>
                      {p.profileId} ({p.provider})
                    </MenuItem>
                  ))}
                </Select>

                <Button variant="contained" onClick={handleBind} disabled={!bind.modelId || !bind.providerProfileId}>
                  Bind
                </Button>

                <Divider />

                <Typography variant="subtitle2">Current model bindings</Typography>
                <Stack spacing={1}>
                  {models.map((m) => (
                    <Box key={m.modelId} sx={{ display: 'flex', justifyContent: 'space-between' }}>
                      <Typography variant="caption">{m.modelId}</Typography>
                      <Typography variant="caption" fontWeight={700}>{m.providerProfileId || '—'}</Typography>
                    </Box>
                  ))}
                </Stack>
              </Stack>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      <Dialog open={openCreate} onClose={() => setOpenCreate(false)} fullWidth maxWidth="sm">
        <DialogTitle>Create Auth Profile</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Select value={form.provider} onChange={(e) => setForm((p) => ({ ...p, provider: String(e.target.value) }))}>
              {providerOptions.map((p) => (
                <MenuItem key={p} value={p}>{p}</MenuItem>
              ))}
            </Select>
            <TextField
              label="Profile ID"
              value={form.profileId}
              onChange={(e) => setForm((p) => ({ ...p, profileId: e.target.value }))}
              placeholder="openai-personal"
            />
            <TextField
              label="Auth Type"
              value={form.authType}
              onChange={(e) => setForm((p) => ({ ...p, authType: e.target.value }))}
            />
            <TextField
              label="Secret / API Key"
              type="password"
              value={form.secret}
              onChange={(e) => setForm((p) => ({ ...p, secret: e.target.value }))}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenCreate(false)}>Cancel</Button>
          <Button variant="contained" onClick={handleCreate} disabled={saving || !form.profileId || !form.secret}>
            {saving ? 'Saving...' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
