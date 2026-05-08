import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import type { WorkflowStep } from '../types';

type Props = {
  open: boolean;
  steps: WorkflowStep[];
  onClose: () => void;
};

export function ExecutionStepsDialog({ open, steps, onClose }: Props) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Execution Steps</DialogTitle>
      <DialogContent>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Activity</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Started</TableCell>
              <TableCell>Completed</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {steps.map((s) => (
              <TableRow key={s.id}>
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
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}
