import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import { Iconify } from 'src/components/iconify';

import { DEFAULT_AI_AGENT_CONFIG } from '../constants';

import type { WorkflowActivityNode } from '../types';

type Props = {
  activities: WorkflowActivityNode[];
  allowedTypes: string[];
  requiredConfigByType: Record<string, string[]>;
  validationErrors: string[];
  onAddActivity: () => void;
  onUpdateActivity: (index: number, patch: Partial<WorkflowActivityNode>) => void;
  onRemoveActivity: (index: number) => void;
  onApplyTypePreset: (index: number, activityType: string) => void;
  onOpenAiConfig: (index: number) => void;
  onAddActivityConfig: (index: number) => void;
  onUpdateActivityConfig: (index: number, key: string, value: string) => void;
  onRemoveActivityConfig: (index: number, key: string) => void;
};

export function WorkflowVisualDesigner({
  activities,
  allowedTypes,
  requiredConfigByType,
  validationErrors,
  onAddActivity,
  onUpdateActivity,
  onRemoveActivity,
  onApplyTypePreset,
  onOpenAiConfig,
  onAddActivityConfig,
  onUpdateActivityConfig,
  onRemoveActivityConfig,
}: Props) {
  return (
    <Card variant="outlined" sx={{ p: 2 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
        <Typography variant="subtitle1">Visual Designer (MVP)</Typography>
        <Button size="small" onClick={onAddActivity} startIcon={<Iconify icon="mingcute:add-line" />}>
          Add Step
        </Button>
      </Stack>
      <Stack spacing={1}>
        {validationErrors.length > 0 && <Alert severity="warning">{validationErrors.slice(0, 3).join(' | ')}</Alert>}
        {activities.map((activity, idx) => (
          <Box key={`${activity.id}_${idx}`} sx={{ p: 1.2, border: 1, borderColor: 'divider', borderRadius: 1 }}>
            <Grid container spacing={1}>
              <Grid item xs={12} md={3}>
                <TextField
                  label="ID"
                  size="small"
                  value={activity.id}
                  onChange={(e) => onUpdateActivity(idx, { id: e.target.value })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={4}>
                <TextField
                  label="Type"
                  size="small"
                  select
                  value={activity.type}
                  onChange={(e) => {
                    const type = e.target.value;
                    onUpdateActivity(idx, {
                      type,
                      aiAgent: type === 'ai.agent' ? (activity.aiAgent ?? { ...DEFAULT_AI_AGENT_CONFIG }) : undefined,
                    });
                  }}
                  fullWidth
                >
                  {allowedTypes.map((type) => (
                    <MenuItem key={type} value={type}>
                      {type}
                    </MenuItem>
                  ))}
                </TextField>
              </Grid>
              <Grid item xs={12} md={3}>
                <TextField
                  label="Name"
                  size="small"
                  value={activity.name ?? ''}
                  onChange={(e) => onUpdateActivity(idx, { name: e.target.value || undefined })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={3}>
                <TextField
                  label="On Success"
                  size="small"
                  value={activity.onSuccess ?? ''}
                  onChange={(e) => onUpdateActivity(idx, { onSuccess: e.target.value || undefined })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={3}>
                <TextField
                  label="On Failure"
                  size="small"
                  value={activity.onFailure ?? ''}
                  onChange={(e) => onUpdateActivity(idx, { onFailure: e.target.value || undefined })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={3}>
                <TextField
                  label="Next"
                  size="small"
                  value={activity.next ?? ''}
                  onChange={(e) => onUpdateActivity(idx, { next: e.target.value || undefined })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={2}>
                <TextField
                  label="Timeout"
                  size="small"
                  type="number"
                  value={activity.timeoutMs ?? 30000}
                  onChange={(e) => onUpdateActivity(idx, { timeoutMs: Number(e.target.value || 30000) })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={2}>
                <TextField
                  label="Retry"
                  size="small"
                  type="number"
                  value={activity.retryCount ?? 0}
                  onChange={(e) => onUpdateActivity(idx, { retryCount: Number(e.target.value || 0) })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={2}>
                <TextField
                  label="Delay"
                  size="small"
                  type="number"
                  value={activity.retryDelayMs ?? 0}
                  onChange={(e) => onUpdateActivity(idx, { retryDelayMs: Number(e.target.value || 0) })}
                  fullWidth
                />
              </Grid>
              <Grid item xs={12} md={2}>
                <Button color="error" variant="outlined" onClick={() => onRemoveActivity(idx)} fullWidth>
                  Remove
                </Button>
              </Grid>
              <Grid item xs={12} md={2}>
                <Button variant="outlined" onClick={() => onApplyTypePreset(idx, activity.type)} fullWidth>
                  Preset
                </Button>
              </Grid>
              {activity.type === 'ai.agent' && (
                <Grid item xs={12} md={2}>
                  <Button variant="outlined" onClick={() => onOpenAiConfig(idx)} fullWidth>
                    AI Config
                  </Button>
                </Grid>
              )}
              <Grid item xs={12}>
                <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 0.8 }}>
                  <Typography variant="caption" color="text.secondary">
                    Config
                    {requiredConfigByType[activity.type]?.length
                      ? ` (required: ${requiredConfigByType[activity.type].join(', ')})`
                      : ''}
                  </Typography>
                  <Button size="small" onClick={() => onAddActivityConfig(idx)}>
                    Add Config
                  </Button>
                </Stack>
                <Stack spacing={0.8}>
                  {Object.entries(activity.config ?? {}).map(([key, value]) => (
                    <Grid container spacing={1} key={`${activity.id}_${key}`}>
                      <Grid item xs={12} md={4}>
                        <TextField
                          label="Key"
                          size="small"
                          value={key}
                          onChange={(e) => {
                            const nextKey = e.target.value.trim();
                            if (!nextKey || nextKey === key) return;
                            const cfg = { ...(activity.config ?? {}) };
                            const currentValue = cfg[key] ?? '';
                            delete cfg[key];
                            cfg[nextKey] = currentValue;
                            onUpdateActivity(idx, { config: cfg });
                          }}
                          fullWidth
                        />
                      </Grid>
                      <Grid item xs={12} md={7}>
                        <TextField
                          label="Value"
                          size="small"
                          value={value}
                          onChange={(e) => onUpdateActivityConfig(idx, key, e.target.value)}
                          fullWidth
                        />
                      </Grid>
                      <Grid item xs={12} md={1}>
                        <Button color="error" onClick={() => onRemoveActivityConfig(idx, key)} fullWidth>
                          X
                        </Button>
                      </Grid>
                    </Grid>
                  ))}
                  {Object.keys(activity.config ?? {}).length === 0 && (
                    <Typography variant="caption" color="text.secondary">
                      No config values yet.
                    </Typography>
                  )}
                </Stack>
              </Grid>
            </Grid>
          </Box>
        ))}
        {activities.length === 0 && <Alert severity="info">No steps found in JSON definition.</Alert>}
      </Stack>
    </Card>
  );
}
