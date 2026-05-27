import Tab from '@mui/material/Tab';
import Grid from '@mui/material/Grid';
import Tabs from '@mui/material/Tabs';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import Checkbox from '@mui/material/Checkbox';
import TextField from '@mui/material/TextField';
import FormGroup from '@mui/material/FormGroup';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import FormControlLabel from '@mui/material/FormControlLabel';

import { normalizeToolLabel } from 'src/aiagentflow/utils/toolLabels';

import { DEFAULT_AI_AGENT_CONFIG } from '../constants';

import type { ToolOption, ModelOption, AiAgentNodeConfig, WorkflowActivityNode } from '../types';

type Props = {
  open: boolean;
  aiTab: number;
  aiTarget: WorkflowActivityNode | null;
  availableModels: ModelOption[];
  availableTools: ToolOption[];
  onTabChange: (value: number) => void;
  onClose: () => void;
  onUpdate: (patch: Partial<AiAgentNodeConfig>) => void;
};

export function AiAgentConfigDialog({
  open,
  aiTab,
  aiTarget,
  availableModels,
  availableTools,
  onTabChange,
  onClose,
  onUpdate,
}: Props) {
  const modelIds = availableModels.map((m) => m.modelId);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Configuración del Nodo de Agente IA</DialogTitle>
      <DialogContent>
        {!aiTarget ? (
          <Alert severity="info" sx={{ mt: 1 }}>
            Selecciona primero un nodo de Agente IA.
          </Alert>
        ) : (
          <Stack spacing={2} sx={{ mt: 1 }}>
            <Tabs value={aiTab} onChange={(_, v) => onTabChange(v)}>
              <Tab label="General" />
              <Tab label="Herramientas" />
              <Tab label="Conocimiento" />
              <Tab label="Avanzado" />
            </Tabs>

            {aiTab === 0 && (
              <Stack spacing={2}>
                <TextField
                  label="Modelo"
                  select
                  value={aiTarget.aiAgent?.model ?? DEFAULT_AI_AGENT_CONFIG.model}
                  onChange={(e) => onUpdate({ model: e.target.value })}
                  disabled={modelIds.length === 0}
                  helperText={modelIds.length === 0 ? 'No hay modelos configurados disponibles.' : undefined}
                  fullWidth
                >
                  {modelIds.map((m) => (
                    <MenuItem key={m} value={m}>
                      {m}
                    </MenuItem>
                  ))}
                </TextField>
                <TextField
                  label="Instrucciones"
                  multiline
                  minRows={8}
                  value={aiTarget.aiAgent?.instructions ?? ''}
                  onChange={(e) => onUpdate({ instructions: e.target.value })}
                  fullWidth
                />
              </Stack>
            )}

            {aiTab === 1 && (
              <FormGroup>
                {(availableTools.length > 0
                  ? availableTools
                  : [{ key: 'http.request', displayName: 'Solicitud HTTP' }]
                ).map((tool) => {
                  const selectedToolsSet = new Set(aiTarget.aiAgent?.tools ?? []);
                  const checked = selectedToolsSet.has(tool.key);
                  return (
                    <FormControlLabel
                      key={tool.key}
                      control={
                        <Checkbox
                          checked={checked}
                          onChange={(e) => {
                            const next = new Set(aiTarget.aiAgent?.tools ?? []);
                            if (e.target.checked) next.add(tool.key);
                            else next.delete(tool.key);
                            onUpdate({ tools: Array.from(next) });
                          }}
                        />
                      }
                      label={normalizeToolLabel(tool.displayName || tool.key)}
                    />
                  );
                })}
              </FormGroup>
            )}

            {aiTab === 2 && (
              <Stack spacing={2}>
                <TextField
                  label="Fuentes de conocimiento (URLs/docs/IDs de datastore separados por coma)"
                  multiline
                  minRows={4}
                  value={(aiTarget.aiAgent?.knowledge ?? []).join(',')}
                  onChange={(e) => onUpdate({ knowledge: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })}
                  fullWidth
                />
                <TextField
                  label="Contexto"
                  multiline
                  minRows={4}
                  value={aiTarget.aiAgent?.context ?? ''}
                  onChange={(e) => onUpdate({ context: e.target.value })}
                  fullWidth
                />
              </Stack>
            )}

            {aiTab === 3 && (
              <Grid container spacing={2}>
                <Grid item xs={12} md={6}>
                  <TextField
                    label="Temperatura"
                    type="number"
                    value={aiTarget.aiAgent?.temperature ?? DEFAULT_AI_AGENT_CONFIG.temperature}
                    onChange={(e) => onUpdate({ temperature: Number(e.target.value || DEFAULT_AI_AGENT_CONFIG.temperature) })}
                    fullWidth
                  />
                </Grid>
                <Grid item xs={12} md={6}>
                  <TextField
                    label="Máximo de tokens"
                    type="number"
                    value={aiTarget.aiAgent?.maxTokens ?? DEFAULT_AI_AGENT_CONFIG.maxTokens}
                    onChange={(e) => onUpdate({ maxTokens: Number(e.target.value || DEFAULT_AI_AGENT_CONFIG.maxTokens) })}
                    fullWidth
                  />
                </Grid>
                <Grid item xs={12} md={6}>
                  <TextField
                    label="Modelo de respaldo"
                    value={aiTarget.aiAgent?.fallbackModel ?? DEFAULT_AI_AGENT_CONFIG.fallbackModel}
                    onChange={(e) => onUpdate({ fallbackModel: e.target.value })}
                    helperText="Opcional. Usa un ID de modelo de Enrutamiento de Modelos si necesitas respaldo."
                    fullWidth
                  />
                </Grid>
                <Grid item xs={12} md={6}>
                  <TextField
                    label="Latencia máxima (ms)"
                    type="number"
                    value={aiTarget.aiAgent?.maxLatencyMs ?? DEFAULT_AI_AGENT_CONFIG.maxLatencyMs}
                    onChange={(e) => onUpdate({ maxLatencyMs: Number(e.target.value || DEFAULT_AI_AGENT_CONFIG.maxLatencyMs) })}
                    fullWidth
                  />
                </Grid>
                <Grid item xs={12} md={6}>
                  <TextField
                    label="Costo máximo (USD)"
                    type="number"
                    value={aiTarget.aiAgent?.maxCostUsd ?? DEFAULT_AI_AGENT_CONFIG.maxCostUsd}
                    onChange={(e) => onUpdate({ maxCostUsd: Number(e.target.value || DEFAULT_AI_AGENT_CONFIG.maxCostUsd) })}
                    fullWidth
                  />
                </Grid>
                <Grid item xs={12}>
                  <FormControlLabel
                    control={
                      <Checkbox
                        checked={aiTarget.aiAgent?.dlpEnabled ?? DEFAULT_AI_AGENT_CONFIG.dlpEnabled}
                        onChange={(e) => onUpdate({ dlpEnabled: e.target.checked })}
                      />
                    }
                    label="Habilitar política básica DLP de respuesta"
                  />
                </Grid>
              </Grid>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cerrar</Button>
      </DialogActions>
    </Dialog>
  );
}
