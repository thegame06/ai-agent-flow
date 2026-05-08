import Chip from '@mui/material/Chip';
import Card from '@mui/material/Card';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';

import { Iconify } from 'src/components/iconify';

import type { WorkflowExecution } from '../types';

type Props = {
  executions: WorkflowExecution[];
  onOpenSteps: (executionId: string) => void;
  onRetryExecution: (executionId: string) => void;
};

export function WorkflowExecutionsCard({ executions, onOpenSteps, onRetryExecution }: Props) {
  return (
    <Card sx={{ p: 2 }}>
      <Typography variant="h6" sx={{ mb: 2 }}>
        Recent Executions
      </Typography>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>ID</TableCell>
            <TableCell>Workflow</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Created</TableCell>
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {executions.slice(0, 12).map((ex) => (
            <TableRow key={ex.id}>
              <TableCell>{ex.id.slice(0, 8)}...</TableCell>
              <TableCell>{ex.workflowDefinitionId.slice(0, 8)}...</TableCell>
              <TableCell>
                <Chip
                  size="small"
                  label={ex.status}
                  color={ex.status === 'Completed' ? 'success' : ex.status === 'Failed' ? 'error' : 'default'}
                />
              </TableCell>
              <TableCell>{new Date(ex.createdAt).toLocaleString()}</TableCell>
              <TableCell align="right">
                <Stack direction="row" spacing={1} justifyContent="flex-end">
                  <IconButton size="small" onClick={() => onOpenSteps(ex.id)}>
                    <Iconify icon="mdi:format-list-bulleted" />
                  </IconButton>
                  {ex.status === 'Failed' && (
                    <Button size="small" color="warning" onClick={() => onRetryExecution(ex.id)}>
                      Retry
                    </Button>
                  )}
                </Stack>
              </TableCell>
            </TableRow>
          ))}
          {executions.length === 0 && (
            <TableRow>
              <TableCell colSpan={5}>
                <Alert severity="info">No workflow executions yet.</Alert>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </Card>
  );
}
