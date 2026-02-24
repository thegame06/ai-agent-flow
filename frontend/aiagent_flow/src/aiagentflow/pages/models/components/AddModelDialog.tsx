import { useState } from 'react';

import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LoadingButton from '@mui/lab/LoadingButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface AddModelDialogProps {
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

export function AddModelDialog({ open, onClose, onSuccess }: AddModelDialogProps) {
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState({
    modelId: '',
    displayName: '',
    providerId: 'OpenAI',
    tier: 'Primary',
    costPer1KTokens: 0.0,
    maxContextTokens: 128000,
    apiKey: '',
  });

  const handleChange = (field: string, value: any) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const handleSubmit = async () => {
    setLoading(true);
    try {
      // Este endpoint probablemente no exista aún en el backend
      // Tendrás que implementarlo o usar un endpoint existente
      await axios.post('/api/v1/model-routing/models', formData);
      
      onSuccess();
      handleClose();
    } catch (error: any) {
      console.error('Failed to add model:', error);
      if (error?.status === 404 || error?.status === 405) {
        alert('Model creation API is not available yet in this backend build.');
      } else {
        alert(error?.message || 'Failed to add model. Check console for details.');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleClose = () => {
    setFormData({
      modelId: '',
      displayName: '',
      providerId: 'OpenAI',
      tier: 'Primary',
      costPer1KTokens: 0.0,
      maxContextTokens: 128000,
      apiKey: '',
    });
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify icon="mingcute:add-line" width={24} />
          Add New Model
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
                placeholder="e.g., gpt-4-turbo"
                required
                helperText="The technical identifier for the model"
              />
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="Display Name"
                value={formData.displayName}
                onChange={(e) => handleChange('displayName', e.target.value)}
                placeholder="e.g., GPT-4 Turbo"
                required
                helperText="Human-readable name"
              />
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                select
                fullWidth
                label="Provider"
                value={formData.providerId}
                onChange={(e) => handleChange('providerId', e.target.value)}
              >
                <MenuItem value="OpenAI">OpenAI</MenuItem>
                <MenuItem value="Anthropic">Anthropic</MenuItem>
                <MenuItem value="Azure">Azure OpenAI</MenuItem>
                <MenuItem value="Google">Google Vertex AI</MenuItem>
                <MenuItem value="AWS">AWS Bedrock</MenuItem>
              </TextField>
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                select
                fullWidth
                label="Tier"
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
                label="Cost per 1K Tokens"
                value={formData.costPer1KTokens}
                onChange={(e) => handleChange('costPer1KTokens', parseFloat(e.target.value))}
                inputProps={{ step: 0.001, min: 0 }}
                helperText="USD cost per 1000 tokens"
              />
            </Grid>

            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                type="number"
                label="Max Context Tokens"
                value={formData.maxContextTokens}
                onChange={(e) => handleChange('maxContextTokens', parseInt(e.target.value, 10))}
                inputProps={{ step: 1000, min: 1000 }}
                helperText="Maximum context window size"
              />
            </Grid>

            <Grid item xs={12}>
              <TextField
                fullWidth
                type="password"
                label="API Key (optional)"
                value={formData.apiKey}
                onChange={(e) => handleChange('apiKey', e.target.value)}
                placeholder="sk-..."
                helperText="Leave empty to use default provider credentials"
              />
            </Grid>

            <Grid item xs={12}>
              <Typography variant="caption" color="text.secondary">
                Note: Model registration currently requires backend configuration. This dialog is a
                Model registration requires backend support. Provider credentials can still be configured at API level in server settings.
              </Typography>
            </Grid>
          </Grid>
        </Box>
      </DialogContent>

      <DialogActions>
        <Button onClick={handleClose} disabled={loading}>
          Cancel
        </Button>
        <LoadingButton
          variant="contained"
          onClick={handleSubmit}
          loading={loading}
          disabled={!formData.modelId || !formData.displayName}
          startIcon={<Iconify icon="mingcute:add-line" />}
        >
          Add Model
        </LoadingButton>
      </DialogActions>
    </Dialog>
  );
}
