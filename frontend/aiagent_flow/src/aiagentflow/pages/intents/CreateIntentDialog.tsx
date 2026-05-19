import { useEffect, useState } from 'react';

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Slider from '@mui/material/Slider';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import type { Agent, Intent, IntentFormData, Workflow } from './types';

// ----------------------------------------------------------------------

interface CreateIntentDialogProps {
  open: boolean;
  intent: Intent | null;
  workflows: Workflow[];
  agents: Agent[];
  onClose: () => void;
  onSave: (data: IntentFormData) => Promise<void>;
}

const CATEGORIES = [
  'Atención al cliente',
  'Ventas',
  'Soporte técnico',
  'Información',
  'Transacciones',
  'Otros',
];

export function CreateIntentDialog({ open, intent, workflows, agents, onClose, onSave }: CreateIntentDialogProps) {
  const [formData, setFormData] = useState<IntentFormData>({
    key: '',
    name: '',
    description: '',
    category: 'Atención al cliente',
    examples: [],
    synonyms: [],
    confidence_threshold: 0.7,
    priority: 5,
    workflow_id: '',
    target_agent_id: '',
    enabled: true,
  });

  const [exampleInput, setExampleInput] = useState('');
  const [synonymInput, setSynonymInput] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (intent) {
      setFormData({
        key: intent.key,
        name: intent.name,
        description: intent.description,
        category: intent.category,
        examples: intent.examples,
        synonyms: intent.synonyms,
        confidence_threshold: intent.confidence_threshold,
        priority: intent.priority,
        workflow_id: intent.workflow_id || '',
        target_agent_id: intent.target_agent_id || '',
        enabled: intent.enabled,
      });
    } else {
      setFormData({
        key: '',
        name: '',
        description: '',
        category: 'Atención al cliente',
        examples: [],
        synonyms: [],
        confidence_threshold: 0.7,
        priority: 5,
        workflow_id: '',
        target_agent_id: '',
        enabled: true,
      });
    }
    setErrors({});
  }, [intent, open]);

  const validate = (): boolean => {
    const newErrors: Record<string, string> = {};
    
    if (!formData.key.trim()) newErrors.key = 'La clave es obligatoria';
    if (!formData.name.trim()) newErrors.name = 'El nombre es obligatorio';
    if (!formData.description.trim()) newErrors.description = 'La descripción es obligatoria';
    if (formData.examples.length === 0) newErrors.examples = 'Se requiere al menos un ejemplo';

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSave = async () => {
    if (!validate()) return;
    
    await onSave(formData);
    onClose();
  };

  const addExample = () => {
    if (exampleInput.trim()) {
      setFormData({ ...formData, examples: [...formData.examples, exampleInput.trim()] });
      setExampleInput('');
      setErrors({ ...errors, examples: '' });
    }
  };

  const removeExample = (index: number) => {
    setFormData({ ...formData, examples: formData.examples.filter((_, i) => i !== index) });
  };

  const addSynonym = () => {
    if (synonymInput.trim()) {
      setFormData({ ...formData, synonyms: [...formData.synonyms, synonymInput.trim()] });
      setSynonymInput('');
    }
  };

  const removeSynonym = (index: number) => {
    setFormData({ ...formData, synonyms: formData.synonyms.filter((_, i) => i !== index) });
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{intent ? 'Editar regla de intención' : 'Nueva regla de intención'}</DialogTitle>
      <Divider />
      
      <DialogContent>
        <Stack spacing={3} sx={{ pt: 2 }}>
          {/* Basic Information */}
          <TextField
            fullWidth
            label="Clave única"
            value={formData.key}
            onChange={(e) => setFormData({ ...formData, key: e.target.value })}
            error={!!errors.key}
            helperText={errors.key || 'Identificador único (ej: solicitud_prestamo)'}
            disabled={!!intent}
          />

          <TextField
            fullWidth
            label="Nombre"
            value={formData.name}
            onChange={(e) => setFormData({ ...formData, name: e.target.value })}
            error={!!errors.name}
            helperText={errors.name || 'Nombre descriptivo de la intención'}
          />

          <TextField
            fullWidth
            multiline
            rows={2}
            label="Descripción"
            value={formData.description}
            onChange={(e) => setFormData({ ...formData, description: e.target.value })}
            error={!!errors.description}
            helperText={errors.description || '¿Qué representa esta intención?'}
          />

          <FormControl fullWidth>
            <InputLabel>Categoría</InputLabel>
            <Select
              value={formData.category}
              label="Categoría"
              onChange={(e) => setFormData({ ...formData, category: e.target.value })}
            >
              {CATEGORIES.map((cat) => (
                <MenuItem key={cat} value={cat}>{cat}</MenuItem>
              ))}
            </Select>
          </FormControl>

          {/* Examples */}
          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Ejemplos {errors.examples && <Typography component="span" color="error" variant="caption">({errors.examples})</Typography>}
            </Typography>
            <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
              <TextField
                fullWidth
                size="small"
                placeholder="Agrega una frase de ejemplo..."
                value={exampleInput}
                onChange={(e) => setExampleInput(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && addExample()}
              />
              <Button variant="outlined" onClick={addExample}>Agregar</Button>
            </Stack>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              {formData.examples.map((example, index) => (
                <Chip
                  key={index}
                  label={example}
                  onDelete={() => removeExample(index)}
                  size="small"
                />
              ))}
            </Stack>
          </Box>

          {/* Synonyms */}
          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>Sinónimos (opcional)</Typography>
            <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
              <TextField
                fullWidth
                size="small"
                placeholder="Agrega un sinónimo..."
                value={synonymInput}
                onChange={(e) => setSynonymInput(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && addSynonym()}
              />
              <Button variant="outlined" onClick={addSynonym}>Agregar</Button>
            </Stack>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              {formData.synonyms.map((synonym, index) => (
                <Chip
                  key={index}
                  label={synonym}
                  onDelete={() => removeSynonym(index)}
                  size="small"
                  variant="outlined"
                />
              ))}
            </Stack>
          </Box>

          {/* Advanced Settings */}
          <Box>
            <Typography variant="subtitle2" sx={{ mb: 2 }}>Prioridad (1 = Más alta)</Typography>
            <Slider
              value={formData.priority}
              onChange={(_, value) => setFormData({ ...formData, priority: value as number })}
              min={1}
              max={10}
              step={1}
              marks
              valueLabelDisplay="auto"
            />
          </Box>

          <Box>
            <Typography variant="subtitle2" sx={{ mb: 2 }}>
              Umbral de confianza: {(formData.confidence_threshold * 100).toFixed(0)}%
            </Typography>
            <Slider
              value={formData.confidence_threshold}
              onChange={(_, value) => setFormData({ ...formData, confidence_threshold: value as number })}
              min={0.5}
              max={1.0}
              step={0.05}
              marks={[
                { value: 0.5, label: '50%' },
                { value: 0.7, label: '70%' },
                { value: 0.9, label: '90%' },
              ]}
              valueLabelDisplay="auto"
              valueLabelFormat={(value) => `${(value * 100).toFixed(0)}%`}
            />
          </Box>

          {/* Workflow Selector */}
          <FormControl fullWidth>
            <InputLabel>Workflow (opcional)</InputLabel>
            <Select
              value={formData.workflow_id || ''}
              label="Workflow (opcional)"
              onChange={(e) => setFormData({ ...formData, workflow_id: e.target.value })}
            >
              <MenuItem value="">Sin workflow</MenuItem>
              {workflows.map((workflow) => (
                <MenuItem key={workflow.id} value={workflow.id}>
                  {workflow.name || workflow.id}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          {/* Agent Selector */}
          <FormControl fullWidth>
            <InputLabel>Agente destino (opcional)</InputLabel>
            <Select
              value={formData.target_agent_id || ''}
              label="Agente destino (opcional)"
              onChange={(e) => setFormData({ ...formData, target_agent_id: e.target.value })}
            >
              <MenuItem value="">Sin agente</MenuItem>
              {agents.map((agent) => (
                <MenuItem key={agent.id} value={agent.id}>
                  {agent.name || agent.id}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControlLabel
            control={
              <Switch
                checked={formData.enabled}
                onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
              />
            }
            label="Activado"
          />
        </Stack>
      </DialogContent>

      <Divider />
      <DialogActions>
        <Button onClick={onClose} color="inherit">
          Cancelar
        </Button>
        <Button onClick={handleSave} variant="contained">
          {intent ? 'Guardar cambios' : 'Crear regla'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
