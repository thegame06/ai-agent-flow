import { useState } from 'react';

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LoadingButton from '@mui/lab/LoadingButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface CloneAgentDialogProps {
  open: boolean;
  onClose: () => void;
  agent: {
    id: string;
    name: string;
  };
  onConfirm: (newName: string, newDescription?: string) => Promise<void>;
}

export function CloneAgentDialog({ open, onClose, agent, onConfirm }: CloneAgentDialogProps) {
  const [newName, setNewName] = useState(`${agent.name} (Copy)`);
  const [newDescription, setNewDescription] = useState('');
  const [loading, setLoading] = useState(false);

  const handleClone = async () => {
    if (!newName.trim()) return;

    setLoading(true);
    try {
      await onConfirm(newName.trim(), newDescription.trim() || undefined);
      handleClose();
    } catch (error) {
      console.error('Clone failed:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleClose = () => {
    setNewName(`${agent.name} (Copy)`);
    setNewDescription('');
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth>
      <DialogTitle>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify icon="solar:copy-bold" width={24} />
          Clone Agent
        </Box>
      </DialogTitle>

      <DialogContent>
        <Box sx={{ pt: 2 }}>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            Create a copy of <strong>{agent.name}</strong>
          </Typography>

          <TextField
            fullWidth
            label="New Agent Name"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            disabled={loading}
            autoFocus
            sx={{ mb: 2 }}
            required
          />

          <TextField
            fullWidth
            multiline
            rows={3}
            label="Description (optional)"
            value={newDescription}
            onChange={(e) => setNewDescription(e.target.value)}
            disabled={loading}
            placeholder="Add a description for the cloned agent..."
          />
        </Box>
      </DialogContent>

      <DialogActions>
        <Button onClick={handleClose} disabled={loading}>
          Cancel
        </Button>
        <LoadingButton
          variant="contained"
          onClick={handleClone}
          loading={loading}
          disabled={!newName.trim()}
          startIcon={<Iconify icon="solar:copy-bold" />}
        >
          Clone Agent
        </LoadingButton>
      </DialogActions>
    </Dialog>
  );
}
