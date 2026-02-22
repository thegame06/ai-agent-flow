import { useState } from 'react';

import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LoadingButton from '@mui/lab/LoadingButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface ExecuteAgentDialogProps {
  open: boolean;
  onClose: () => void;
  agent: {
    id: string;
    name: string;
    description?: string;
  };
}

interface ExecutionResult {
  executionId: string;
  status: 'Running' | 'Completed' | 'Failed';
  output?: string;
  error?: string;
}

export function ExecuteAgentDialog({ open, onClose, agent }: ExecuteAgentDialogProps) {
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<ExecutionResult | null>(null);

  const handleExecute = async () => {
    if (!message.trim()) return;

    setLoading(true);
    setResult(null);

    try {
      console.log('Executing agent:', agent.id, 'with message:', message);
      
      // POST /api/v1/tenants/{tenantId}/agents/{agentId}/trigger
      const response = await axios.post(`/api/v1/tenants/tenant-1/agents/${agent.id}/trigger`, {
        message: message.trim(),
        context: {},
      });

      console.log('Trigger response:', response.data);

      const executionId = response.data.executionId || response.data.id;

      if (!executionId) {
        setResult({
          executionId: '',
          status: 'Failed',
          error: 'No execution ID returned from server',
        });
        setLoading(false);
        return;
      }

      // Poll for execution status
      let attempts = 0;
      const maxAttempts = 30; // 30 seconds max
      const checkStatus = setInterval(async () => {
        attempts += 1;
        try {
          const statusResponse = await axios.get(
            `/api/v1/tenants/tenant-1/executions/${executionId}`
          );
          const execution = statusResponse.data;

          console.log(`Polling attempt ${attempts}:`, execution);

          if (
            execution.status === 'Completed' ||
            execution.status === 'Failed' ||
            attempts >= maxAttempts
          ) {
            clearInterval(checkStatus);
            setResult({
              executionId,
              status: execution.status,
              output: execution.output || execution.result?.content || execution.result?.message,
              error: execution.error || execution.errorMessage,
            });
            setLoading(false);
          }
        } catch (error: any) {
          console.error('Polling error:', error);
          clearInterval(checkStatus);
          setResult({
            executionId,
            status: 'Failed',
            error: `Unable to fetch execution status: ${error.message}`,
          });
          setLoading(false);
        }
      }, 1000);
    } catch (error: any) {
      console.error('Execution error:', error);
      setResult({
        executionId: '',
        status: 'Failed',
        error:
          error.response?.data?.message ||
          error.response?.data?.error ||
          error.message ||
          'Failed to execute agent',
      });
      setLoading(false);
    }
  };

  const handleClose = () => {
    setMessage('');
    setResult(null);
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Iconify icon="mdi:rocket-launch-outline" width={24} />
          Execute Agent: {agent.name}
        </Box>
      </DialogTitle>

      <DialogContent>
        <Stack spacing={3} sx={{ pt: 2 }}>
          {agent.description && (
            <Typography variant="body2" color="text.secondary">
              {agent.description}
            </Typography>
          )}

          <TextField
            fullWidth
            multiline
            rows={4}
            label="Message / Task"
            placeholder="Enter the task or message for the agent to process..."
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            disabled={loading}
            helperText="Provide clear instructions or a question for the agent"
          />

          {loading && (
            <Box>
              <Typography variant="body2" color="text.secondary" gutterBottom>
                Executing agent...
              </Typography>
              <LinearProgress />
            </Box>
          )}

          {result && (
            <Alert severity={result.status === 'Completed' ? 'success' : 'error'} sx={{ mt: 2 }}>
              <Stack spacing={1}>
                <Box>
                  <Typography variant="subtitle2">
                    Status: {result.status}
                  </Typography>
                  {result.executionId && (
                    <Typography variant="caption" color="text.secondary">
                      Execution ID: {result.executionId}
                    </Typography>
                  )}
                </Box>

                {result.output && (
                  <Box>
                    <Typography variant="body2" fontWeight={600} gutterBottom>
                      Output:
                    </Typography>
                    <Typography
                      variant="body2"
                      component="pre"
                      sx={{
                        whiteSpace: 'pre-wrap',
                        wordBreak: 'break-word',
                        fontFamily: 'monospace',
                        fontSize: '0.875rem',
                      }}
                    >
                      {result.output}
                    </Typography>
                  </Box>
                )}

                {result.error && (
                  <Box>
                    <Typography variant="body2" fontWeight={600} gutterBottom>
                      Error:
                    </Typography>
                    <Typography variant="body2" color="error">
                      {result.error}
                    </Typography>
                  </Box>
                )}
              </Stack>
            </Alert>
          )}
        </Stack>
      </DialogContent>

      <DialogActions>
        <Button onClick={handleClose} disabled={loading}>
          {result ? 'Close' : 'Cancel'}
        </Button>
        <LoadingButton
          variant="contained"
          onClick={handleExecute}
          loading={loading}
          disabled={!message.trim() || loading}
          startIcon={<Iconify icon="mdi:play" />}
        >
          Execute
        </LoadingButton>
      </DialogActions>
    </Dialog>
  );
}
