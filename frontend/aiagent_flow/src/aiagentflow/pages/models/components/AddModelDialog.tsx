import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import InputLabel from '@mui/material/InputLabel';
import Typography from '@mui/material/Typography';
import LoadingButton from '@mui/lab/LoadingButton';
import FormControl from '@mui/material/FormControl';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

interface ModelFormData {
  modelId: string;
  displayName: string;
  providerId: string;
  tier: string;
  costPer1KTokens: number;
  maxContextTokens: number;
  providerProfileId: string;
  apiKey: string;
}

interface AuthProfileOption {
  id: string;
  provider: string;
  profileId: string;
  secretMasked?: string;
}

interface ModelDraft {
  modelId: string;
  displayName: string;
  providerId: string;
  tier: string;
  costPer1KTokens: number;
  maxContextTokens: number;
  providerProfileId?: string;
}

interface AddModelDialogProps {
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
  tenantId: string;
  initialModel?: ModelDraft | null;
}

const providerOptions = ['OpenAI', 'Anthropic', 'Gemini', 'OpenRouter', 'Groq', 'Deepgram', '11Labs'];

const emptyForm: ModelFormData = {
  modelId: '',
  displayName: '',
  providerId: 'OpenAI',
  tier: 'Primary',
  costPer1KTokens: 0,
  maxContextTokens: 128000,
  providerProfileId: '',
  apiKey: '',
};

export function AddModelDialog({
  open,
  onClose,
  onSuccess,
  tenantId,
  initialModel,
}: AddModelDialogProps) {
  const [loading, setLoading] = useState(false);
  const [profiles, setProfiles] = useState<AuthProfileOption[]>([]);
  const [profilesError, setProfilesError] = useState<string | null>(null);
  const [formData, setFormData] = useState<ModelFormData>(emptyForm);

  useEffect(() => {
    if (!open) return;

    setFormData(
      initialModel
        ? {
            modelId: initialModel.modelId,
            displayName: initialModel.displayName,
            providerId: initialModel.providerId || 'OpenAI',
            tier: initialModel.tier || 'Primary',
            costPer1KTokens: Number(initialModel.costPer1KTokens ?? 0),
            maxContextTokens: Number(initialModel.maxContextTokens ?? 128000),
            providerProfileId: initialModel.providerProfileId ?? '',
            apiKey: '',
          }
        : emptyForm
    );
  }, [initialModel, open]);

  useEffect(() => {
    if (!open) return;

    const loadProfiles = async () => {
      try {
        setProfilesError(null);
        const res = await axios.get(`/api/v1/tenants/${tenantId}/auth-profiles`);
        setProfiles(Array.isArray(res.data) ? res.data : []);
      } catch (error: any) {
        setProfiles([]);
        setProfilesError(error?.message || 'No se pudieron cargar los perfiles de proveedor.');
      }
    };

    void loadProfiles();
  }, [open, tenantId]);

  const filteredProfiles = useMemo(
    () =>
      profiles.filter(
        (profile) => profile.provider?.toLowerCase() === formData.providerId.toLowerCase()
      ),
    [formData.providerId, profiles]
  );

  const handleChange = <K extends keyof ModelFormData>(field: K, value: ModelFormData[K]) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const handleSubmit = async () => {
    setLoading(true);
    try {
      await axios.post('/api/v1/model-routing/models', {
        modelId: formData.modelId.trim(),
        displayName: formData.displayName.trim(),
        providerId: formData.providerId,
        tier: formData.tier,
        costPer1KTokens: Number(formData.costPer1KTokens),
        maxContextTokens: Number(formData.maxContextTokens),
        providerProfileId: formData.providerProfileId || undefined,
        apiKey: formData.apiKey || undefined,
      });

      onSuccess();
      handleClose();
    } catch (error: any) {
      alert(error?.message || 'No se pudo guardar el modelo.');
    } finally {
      setLoading(false);
    }
  };

  const handleClose = () => {
    setFormData(emptyForm);
    setProfilesError(null);
    onClose();
  };

  const isEdit = Boolean(initialModel);
  const canSubmit = Boolean(
    formData.modelId &&
      formData.displayName &&
      (isEdit || formData.providerProfileId || formData.apiKey)
  );

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify icon={isEdit ? 'solar:pen-outline' : 'mingcute:add-line'} width={24} />
          {isEdit ? 'Editar modelo' : 'Agregar nuevo modelo'}
        </Box>
      </DialogTitle>

      <DialogContent>
        <Box sx={{ pt: 2 }}>
          <Grid container spacing={2}>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="Model ID"
                value={formData.modelId}
                onChange={(e) => handleChange('modelId', e.target.value)}
                placeholder="gpt-4o-mini"
                required
                disabled={isEdit}
                helperText="Identificador tecnico del modelo."
              />
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="Display Name"
                value={formData.displayName}
                onChange={(e) => handleChange('displayName', e.target.value)}
                placeholder="GPT-4o mini"
                required
                helperText="Nombre visible en la plataforma."
              />
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                select
                fullWidth
                label="Proveedor"
                value={formData.providerId}
                onChange={(e) => {
                  handleChange('providerId', e.target.value);
                  handleChange('providerProfileId', '');
                }}
              >
                {providerOptions.map((provider) => (
                  <MenuItem key={provider} value={provider}>
                    {provider}
                  </MenuItem>
                ))}
              </TextField>
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                select
                fullWidth
                label="Prioridad"
                value={formData.tier}
                onChange={(e) => handleChange('tier', e.target.value)}
              >
                <MenuItem value="Primary">Primary</MenuItem>
                <MenuItem value="Fallback">Fallback</MenuItem>
                <MenuItem value="Secondary">Secondary</MenuItem>
              </TextField>
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                type="number"
                label="Costo por 1K tokens"
                value={formData.costPer1KTokens}
                onChange={(e) => handleChange('costPer1KTokens', Number(e.target.value))}
                inputProps={{ step: 0.001, min: 0 }}
                helperText="Costo estimado por 1000 tokens."
              />
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                type="number"
                label="Contexto maximo"
                value={formData.maxContextTokens}
                onChange={(e) => handleChange('maxContextTokens', Number(e.target.value))}
                inputProps={{ step: 1000, min: 0 }}
                helperText="Ventana maxima de contexto."
              />
            </Grid>

            <Grid item xs={12}>
              <FormControl fullWidth>
                <InputLabel>Provider Auth Profile</InputLabel>
                <Select
                  value={formData.providerProfileId}
                  label="Provider Auth Profile"
                  onChange={(e) => handleChange('providerProfileId', String(e.target.value))}
                >
                  <MenuItem value="">
                    <em>Seleccionar perfil existente</em>
                  </MenuItem>
                  {filteredProfiles.map((profile) => (
                    <MenuItem key={profile.id} value={profile.profileId}>
                      {profile.profileId}
                      {profile.secretMasked ? ` · ${profile.secretMasked}` : ''}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Grid>

            {profilesError && (
              <Grid item xs={12}>
                <Alert severity="warning">{profilesError}</Alert>
              </Grid>
            )}

            <Grid item xs={12}>
              <TextField
                fullWidth
                type="password"
                label="API Key manual"
                value={formData.apiKey}
                onChange={(e) => handleChange('apiKey', e.target.value)}
                placeholder="sk-..."
                helperText="Opcional. Si no eliges un perfil existente, se crea y vincula uno nuevo con esta clave."
              />
            </Grid>

            <Grid item xs={12}>
              <Typography variant="caption" color="text.secondary">
                El modelo puede usar un perfil existente del mismo proveedor o crear uno nuevo con la API key manual.
              </Typography>
            </Grid>
          </Grid>
        </Box>
      </DialogContent>

      <DialogActions>
        <Button onClick={handleClose} disabled={loading}>
          Cancelar
        </Button>
        <LoadingButton
          variant="contained"
          onClick={handleSubmit}
          loading={loading}
          disabled={!canSubmit}
          startIcon={<Iconify icon={isEdit ? 'solar:pen-outline' : 'mingcute:add-line'} />}
        >
          {isEdit ? 'Guardar cambios' : 'Agregar modelo'}
        </LoadingButton>
      </DialogActions>
    </Dialog>
  );
}
