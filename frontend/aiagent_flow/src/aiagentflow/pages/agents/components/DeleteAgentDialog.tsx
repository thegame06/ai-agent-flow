import { useState } from 'react';

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Typography from '@mui/material/Typography';
import LoadingButton from '@mui/lab/LoadingButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface DeleteAgentDialogProps {
  open: boolean;
  onClose: () => void;
  agent: {
    id: string;
    name: string;
  };
  onConfirm: () => Promise<void>;
}

export function DeleteAgentDialog({ open, onClose, agent, onConfirm }: DeleteAgentDialogProps) {
  const [loading, setLoading] = useState(false);

  const handleDelete = async () => {
    setLoading(true);
    try {
      await onConfirm();
      onClose();
    } catch (error) {
      console.error('Delete failed:', error);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify icon="solar:trash-bin-trash-bold" width={24} color="error.main" />
          Delete Agent
        </Box>
      </DialogTitle>

      <DialogContent>
        <Typography variant="body2" color="text.secondary">
          Are you sure you want to delete <strong>{agent.name}</strong>?
        </Typography>
        <Typography variant="body2" color="error.main" sx={{ mt: 2 }}>
          This action cannot be undone.
        </Typography>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose} disabled={loading}>
          Cancel
        </Button>
        <LoadingButton
          color="error"
          variant="contained"
          onClick={handleDelete}
          loading={loading}
          startIcon={<Iconify icon="solar:trash-bin-trash-bold" />}
        >
          Delete
        </LoadingButton>
      </DialogActions>
    </Dialog>
  );
}
