import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';

import axios, { endpoints } from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

type RuntimeProfile = {
  id: string;
  tenantId: string;
  name: string;
  runtimeKind: 'Text' | 'Voice' | 'MultimodalRealtime' | string;
  roles: Record<string, string>;
  isDefault?: boolean;
};

type ModelCatalogEntry = {
  modelId: string;
  displayName?: string;
  providerId?: string;
  status?: string;
};

type Props = {
  tenantId: string;
  runtimeKind: 'Text' | 'Voice' | 'MultimodalRealtime';
};

const emptyDraft = (runtimeKind: Props['runtimeKind']) => ({
  id: '',
  name: '',
  runtimeKind,
  isDefault: false,
  brain: '',
  stt: '',
  tts: '',
});

export function RuntimeModelProfilesPanel({ tenantId, runtimeKind }: Props) {
  const [profiles, setProfiles] = useState<RuntimeProfile[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [models, setModels] = useState<ModelCatalogEntry[]>([]);
  const [draft, setDraft] = useState(emptyDraft(runtimeKind));

  const endpoint = useMemo(() => endpoints.agentflow.runtimeModelProfiles.list(tenantId), [tenantId]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError('');
      const res = await axios.get(endpoint, { params: { runtimeKind } });
      setProfiles(Array.isArray(res.data) ? res.data : []);
    } catch (e: any) {
      setError(e?.response?.data?.message ?? e?.message ?? 'No se pudieron cargar los perfiles de modalidad.');
    } finally {
      setLoading(false);
    }
  }, [endpoint, runtimeKind]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const loadModels = async () => {
      try {
        const res = await axios.get(endpoints.agentflow.models.list);
        setModels(Array.isArray(res.data) ? res.data : []);
      } catch {
        setModels([]);
      }
    };

    void loadModels();
  }, []);

  useEffect(() => {
    setEditingId(null);
    setDraft(emptyDraft(runtimeKind));
  }, [runtimeKind]);

  const openNew = () => {
    setEditingId(null);
    setDraft(emptyDraft(runtimeKind));
    setOpen(true);
  };

  const openEdit = (profile: RuntimeProfile) => {
    setEditingId(profile.id);
    setDraft({
      id: profile.id,
      name: profile.name,
      runtimeKind: (profile.runtimeKind as Props['runtimeKind']) ?? runtimeKind,
      isDefault: !!profile.isDefault,
      brain: profile.roles?.brain ?? '',
      stt: profile.roles?.stt ?? '',
      tts: profile.roles?.tts ?? '',
    });
    setOpen(true);
  };

  const openDuplicate = (profile: RuntimeProfile) => {
    setEditingId(null);
    setDraft({
      id: `${profile.id}-copy`,
      name: `${profile.name} (copia)`,
      runtimeKind: (profile.runtimeKind as Props['runtimeKind']) ?? runtimeKind,
      isDefault: false,
      brain: profile.roles?.brain ?? '',
      stt: profile.roles?.stt ?? '',
      tts: profile.roles?.tts ?? '',
    });
    setOpen(true);
  };

  const handleSave = async () => {
    if (!draft.id.trim() || !draft.name.trim() || !draft.brain.trim()) {
      setError('Completa ID, nombre y modelo de cerebro.');
      return;
    }

    try {
      setSaving(true);
      setError('');
      const roles: Record<string, string> = { brain: draft.brain.trim() };
      if (draft.runtimeKind !== 'Text') {
        if (draft.stt.trim()) roles.stt = draft.stt.trim();
        if (draft.tts.trim()) roles.tts = draft.tts.trim();
      }

      await axios.put(`${endpoint}/${encodeURIComponent(draft.id.trim())}`, {
        name: draft.name.trim(),
        runtimeKind: draft.runtimeKind,
        roles,
        isDefault: draft.isDefault,
        metadata: { managedFrom: 'runtime-studio-ui' },
      });

      setOpen(false);
      setEditingId(null);
      setDraft(emptyDraft(runtimeKind));
      await load();
    } catch (e: any) {
      setError(e?.response?.data?.message ?? e?.message ?? 'No se pudo guardar el perfil de modalidad.');
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await axios.delete(`${endpoint}/${encodeURIComponent(id)}`);
      await load();
    } catch (e: any) {
      setError(e?.response?.data?.message ?? e?.message ?? 'No se pudo eliminar el perfil de modalidad.');
    }
  };

  const availableRoleModels = useMemo(() => {
    if (draft.runtimeKind === 'Text') {
      return { brain: models, stt: [] as ModelCatalogEntry[], tts: [] as ModelCatalogEntry[] };
    }

    return {
      brain: models,
      stt: models,
      tts: models,
    };
  }, [draft.runtimeKind, models]);

  const renderModelOptionLabel = (model: ModelCatalogEntry) =>
    `${model.displayName || model.modelId}${model.providerId ? ` · ${model.providerId}` : ''}`;

  const runtimeLabel =
    runtimeKind === 'Text' ? 'texto' : runtimeKind === 'Voice' ? 'voz' : 'multimodal';

  return (
    <Card sx={{ p: 2 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1.5 }}>
        <Box>
          <Typography variant="subtitle1">Perfiles de modelos por modalidad</Typography>
          <Typography variant="caption" color="text.secondary">
            Define los modelos por rol para {runtimeLabel}: cerebro, transcripcion y voz.
          </Typography>
        </Box>
        <Button
          variant="contained"
          size="small"
          startIcon={<Iconify icon="mingcute:add-line" />}
          onClick={openNew}
        >
          Nuevo perfil
        </Button>
      </Stack>

      {error && <Alert severity="error" sx={{ mb: 1.5 }}>{error}</Alert>}
      {loading && <Typography variant="body2" color="text.secondary">Cargando perfiles...</Typography>}

      <Stack spacing={1}>
        {profiles.map((profile) => (
          <Card key={profile.id} variant="outlined" sx={{ p: 1.5 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                <Typography variant="body2" fontWeight={700}>{profile.name}</Typography>
                <Chip size="small" label={profile.id} />
                <Chip size="small" label={profile.runtimeKind} color="info" />
                {profile.isDefault && <Chip size="small" color="success" label="Predeterminado" />}
                <Chip size="small" label={`cerebro: ${profile.roles?.brain ?? '-'}`} />
                {profile.runtimeKind !== 'Text' && <Chip size="small" label={`stt: ${profile.roles?.stt ?? '-'}`} />}
                {profile.runtimeKind !== 'Text' && <Chip size="small" label={`tts: ${profile.roles?.tts ?? '-'}`} />}
              </Stack>
              <Stack direction="row" spacing={0.5}>
                <IconButton size="small" onClick={() => openEdit(profile)}>
                  <Iconify icon="solar:pen-outline" width={18} />
                </IconButton>
                <IconButton size="small" onClick={() => openDuplicate(profile)}>
                  <Iconify icon="solar:copy-outline" width={18} />
                </IconButton>
                <IconButton size="small" onClick={() => void handleDelete(profile.id)}>
                  <Iconify icon="solar:trash-bin-trash-outline" width={18} />
                </IconButton>
              </Stack>
            </Stack>
          </Card>
        ))}

        {!loading && profiles.length === 0 && (
          <Typography variant="body2" color="text.secondary">
            Aun no hay perfiles para esta modalidad.
          </Typography>
        )}
      </Stack>

      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editingId ? 'Editar perfil de modalidad' : 'Nuevo perfil de modalidad'}</DialogTitle>
        <DialogContent>
          <Stack spacing={1.5} sx={{ mt: 1 }}>
            <TextField
              label="ID tecnico"
              value={draft.id}
              onChange={(e) => setDraft((prev) => ({ ...prev, id: e.target.value }))}
              disabled={!!editingId}
            />
            <TextField
              label="Nombre"
              value={draft.name}
              onChange={(e) => setDraft((prev) => ({ ...prev, name: e.target.value }))}
            />
            <Select
              value={draft.runtimeKind}
              onChange={(e) => setDraft((prev) => ({ ...prev, runtimeKind: e.target.value as Props['runtimeKind'] }))}
            >
              <MenuItem value="Text">Texto</MenuItem>
              <MenuItem value="Voice">Voz</MenuItem>
              <MenuItem value="MultimodalRealtime">Multimodal</MenuItem>
            </Select>
            <TextField
              select
              label="Modelo de cerebro"
              value={draft.brain}
              onChange={(e) => setDraft((prev) => ({ ...prev, brain: e.target.value }))}
              helperText="Selecciona un modelo ya registrado en el catalogo."
            >
              <MenuItem value=""><em>Seleccionar modelo</em></MenuItem>
              {availableRoleModels.brain.map((model) => (
                <MenuItem key={model.modelId} value={model.modelId}>
                  {renderModelOptionLabel(model)}
                </MenuItem>
              ))}
            </TextField>
            {draft.runtimeKind !== 'Text' && (
              <TextField
                select
                label="Modelo STT"
                value={draft.stt}
                onChange={(e) => setDraft((prev) => ({ ...prev, stt: e.target.value }))}
                helperText="Selecciona el modelo parametrizado para transcripcion."
              >
                <MenuItem value=""><em>Seleccionar modelo</em></MenuItem>
                {availableRoleModels.stt.map((model) => (
                  <MenuItem key={model.modelId} value={model.modelId}>
                    {renderModelOptionLabel(model)}
                  </MenuItem>
                ))}
              </TextField>
            )}
            {draft.runtimeKind !== 'Text' && (
              <TextField
                select
                label="Modelo TTS"
                value={draft.tts}
                onChange={(e) => setDraft((prev) => ({ ...prev, tts: e.target.value }))}
                helperText="Selecciona el modelo parametrizado para salida de voz."
              >
                <MenuItem value=""><em>Seleccionar modelo</em></MenuItem>
                {availableRoleModels.tts.map((model) => (
                  <MenuItem key={model.modelId} value={model.modelId}>
                    {renderModelOptionLabel(model)}
                  </MenuItem>
                ))}
              </TextField>
            )}
            <Button
              variant={draft.isDefault ? 'contained' : 'outlined'}
              onClick={() => setDraft((prev) => ({ ...prev, isDefault: !prev.isDefault }))}
            >
              {draft.isDefault ? 'Predeterminado activo' : 'Marcar como predeterminado'}
            </Button>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={() => void handleSave()} disabled={saving}>
            {saving ? 'Guardando...' : 'Guardar'}
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  );
}
