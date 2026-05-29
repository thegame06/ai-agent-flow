import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import { Iconify } from 'src/components/iconify';

import type { WorkflowStep } from '../types';

type Props = {
  open: boolean;
  steps: WorkflowStep[];
  onClose: () => void;
};

export function ExecutionStepsDialog({ open, steps, onClose }: Props) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle sx={{ pb: 1 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
          <Box>
            <Typography variant="h6">Pasos de ejecucion</Typography>
            <Typography variant="body2" color="text.secondary">
              Revisa el avance y el resultado de cada actividad ejecutada.
            </Typography>
          </Box>
          <IconButton onClick={onClose}>
            <Iconify icon="mdi:close" />
          </IconButton>
        </Stack>
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Alert severity="info">{steps.length} paso(s) registrados para esta ejecucion.</Alert>
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Actividad</TableCell>
                  <TableCell>Estado</TableCell>
                  <TableCell>Inicio</TableCell>
                  <TableCell>Completado</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {steps.map((s) => (
                  <TableRow key={s.id} hover>
                    <TableCell>
                      <Typography variant="body2">{s.activityName}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        {s.activityType}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={s.status}
                        color={s.status === 'Completed' ? 'success' : s.status === 'Failed' ? 'error' : 'default'}
                      />
                    </TableCell>
                    <TableCell>{new Date(s.startedAt).toLocaleString()}</TableCell>
                    <TableCell>{s.completedAt ? new Date(s.completedAt).toLocaleString() : '-'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cerrar</Button>
      </DialogActions>
    </Dialog>
  );
}
