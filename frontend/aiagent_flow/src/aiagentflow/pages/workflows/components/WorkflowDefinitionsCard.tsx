import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';

import { workflowStatusLabel } from '../constants';

import type { WorkflowDefinition } from '../types';

type Props = {
  loading: boolean;
  workflows: WorkflowDefinition[];
  selectedId?: string | null;
  onSelect: (wf: WorkflowDefinition) => void;
};

export function WorkflowDefinitionsCard({ loading, workflows, selectedId, onSelect }: Props) {
  return (
    <Card sx={{ p: 2 }}>
      <Typography variant="h6" sx={{ mb: 2 }}>
        Mis flujos
      </Typography>
      {loading ? (
        <Box sx={{ py: 4, textAlign: 'center' }}>
          <CircularProgress />
        </Box>
      ) : workflows.length === 0 ? (
        <Alert severity="info">Aun no hay flujos guardados. Crea uno desde una plantilla o inicia uno nuevo.</Alert>
      ) : (
        <Stack spacing={1}>
          {workflows.map((wf) => (
            <Box
              key={wf.id}
              sx={{
                p: 1.5,
                border: 1,
                borderColor: selectedId === wf.id ? 'primary.main' : 'divider',
                borderRadius: 1,
                bgcolor: selectedId === wf.id ? 'action.selected' : 'background.paper',
                cursor: 'pointer',
              }}
              onClick={() => onSelect(wf)}
            >
              <Typography variant="subtitle2">{wf.name}</Typography>
              <Typography variant="caption" color="text.secondary" display="block">
                Evento: {wf.triggerEventName}
              </Typography>
              <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                <Chip
                  size="small"
                  label={workflowStatusLabel(wf.status)}
                  color={wf.status === 'Published' ? 'success' : 'default'}
                />
                <Chip size="small" variant="outlined" label={`v${wf.version}`} />
              </Stack>
            </Box>
          ))}
        </Stack>
      )}
    </Card>
  );
}

