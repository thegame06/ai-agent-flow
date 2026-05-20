import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Switch from '@mui/material/Switch';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import { Iconify } from 'src/components/iconify';

import type { Intent } from './types';

interface IntentsListProps {
  intents: Intent[];
  loading: boolean;
  onEdit: (intent: Intent) => void;
  onToggle: (intentId: string, enabled: boolean) => void;
  onDelete: (intentId: string) => void;
}

export function IntentsList({ intents, loading, onEdit, onToggle, onDelete }: IntentsListProps) {
  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight={400}>
        <CircularProgress />
      </Box>
    );
  }

  if (intents.length === 0) {
    return (
      <Card sx={{ p: 5, textAlign: 'center' }}>
        <Iconify icon="eva:inbox-outline" width={64} sx={{ mx: 'auto', mb: 2, color: 'text.disabled' }} />
        <Typography variant="h6" color="text.secondary">
          No hay reglas configuradas
        </Typography>
        <Typography variant="body2" color="text.disabled" sx={{ mt: 1 }}>
          Crea tu primera regla de intención para comenzar
        </Typography>
      </Card>
    );
  }

  return (
    <Card>
      <TableContainer>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Intención</TableCell>
              <TableCell>Categoría</TableCell>
              <TableCell>Canal</TableCell>
              <TableCell>Workflow</TableCell>
              <TableCell>Agente destino</TableCell>
              <TableCell>Ejemplos</TableCell>
              <TableCell>Prioridad</TableCell>
              <TableCell>Confianza</TableCell>
              <TableCell>Activo</TableCell>
              <TableCell align="right">Acciones</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {intents.map((intent) => (
              <TableRow key={intent.id} hover>
                <TableCell>
                  <Stack spacing={0.5}>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Typography variant="subtitle2">{intent.name}</Typography>
                      {intent.is_base_intent && <Chip label="BASE" size="small" color="primary" sx={{ height: 20 }} />}
                    </Stack>
                    <Typography variant="caption" color="text.secondary" noWrap sx={{ maxWidth: 300 }}>
                      {intent.description}
                    </Typography>
                    <Typography variant="caption" color="text.disabled">Clave: {intent.key}</Typography>
                  </Stack>
                </TableCell>
                <TableCell>
                  <Chip label={intent.category} size="small" color="default" variant="outlined" />
                </TableCell>
                <TableCell>
                  <Typography variant="body2" color="text.secondary">{intent.channel || 'Todos'}</Typography>
                </TableCell>
                <TableCell>
                  <Stack spacing={0.5}>
                    {intent.workflow_name ? (
                      <>
                        <Typography variant="body2">{intent.workflow_name}</Typography>
                        <Typography variant="caption" color="text.disabled">{intent.workflow_id}</Typography>
                      </>
                    ) : (
                      <Typography variant="caption" color="text.disabled">Sin workflow</Typography>
                    )}
                  </Stack>
                </TableCell>
                <TableCell>
                  {intent.target_agent_id ? (
                    <Typography variant="body2" color="text.secondary">{intent.target_agent_id}</Typography>
                  ) : (
                    <Typography variant="caption" color="text.disabled">Sin agente</Typography>
                  )}
                </TableCell>
                <TableCell>
                  <Typography variant="body2" color="text.secondary">{intent.examples.length} ejemplos</Typography>
                </TableCell>
                <TableCell>
                  <Chip label={`P${intent.priority}`} size="small" color={intent.priority <= 2 ? 'error' : intent.priority <= 4 ? 'warning' : 'default'} />
                </TableCell>
                <TableCell>
                  <Typography variant="body2">{(intent.confidence_threshold * 100).toFixed(0)}%</Typography>
                </TableCell>
                <TableCell>
                  <Switch checked={intent.enabled} onChange={(e) => onToggle(intent.id, e.target.checked)} size="small" />
                </TableCell>
                <TableCell align="right">
                  <IconButton size="small" onClick={() => onEdit(intent)}>
                    <Iconify icon="eva:edit-outline" />
                  </IconButton>
                  <IconButton size="small" onClick={() => onDelete(intent.id)} disabled={intent.is_base_intent}>
                    <Iconify icon="eva:trash-2-outline" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Card>
  );
}
