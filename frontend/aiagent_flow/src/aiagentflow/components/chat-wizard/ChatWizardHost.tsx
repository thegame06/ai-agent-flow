import type { KeyboardEvent } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import CircularProgress from '@mui/material/CircularProgress';

import { Iconify } from 'src/components/iconify';

export type ChatWizardMessage = {
  role: 'assistant' | 'user' | 'system';
  content: string;
};

type ChatWizardHostProps = {
  title: string;
  subtitle: string;
  messages: ChatWizardMessage[];
  inputValue: string;
  inputPlaceholder: string;
  loading?: boolean;
  sendDisabled?: boolean;
  onInputChange: (value: string) => void;
  onSend: () => void;
  children?: React.ReactNode;
};

export function ChatWizardHost({
  title,
  subtitle,
  messages,
  inputValue,
  inputPlaceholder,
  loading = false,
  sendDisabled = false,
  onInputChange,
  onSend,
  children,
}: ChatWizardHostProps) {
  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      onSend();
    }
  };

  return (
    <Card sx={{ p: 2.5, borderRadius: 3 }}>
      <Stack spacing={2}>
        <Box>
          <Typography variant="h5">{title}</Typography>
          <Typography variant="body2" color="text.secondary">
            {subtitle}
          </Typography>
        </Box>

        <Box sx={{ p: 1.25, border: '1px solid', borderColor: 'divider', borderRadius: 2, maxHeight: 380, overflow: 'auto' }}>
          <Stack spacing={1}>
            {messages.map((message, index) => (
              <Box
                 
                key={index}
                sx={{
                  p: 1.25,
                  borderRadius: 1.5,
                  alignSelf: message.role === 'user' ? 'flex-end' : 'flex-start',
                  bgcolor: message.role === 'user' ? 'primary.lighter' : message.role === 'system' ? 'warning.lighter' : 'background.neutral',
                  maxWidth: '88%',
                }}
              >
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                  {message.content}
                </Typography>
              </Box>
            ))}
            {loading && (
              <Stack direction="row" spacing={1} alignItems="center" sx={{ pl: 0.5 }}>
                <CircularProgress size={14} />
                <Typography variant="caption" color="text.secondary">
                  Ejecutando paso...
                </Typography>
              </Stack>
            )}
          </Stack>
        </Box>

        {children}

        <Stack direction="row" spacing={1}>
          <TextField
            fullWidth
            size="small"
            value={inputValue}
            onChange={(e) => onInputChange(e.target.value)}
            onKeyDown={onKeyDown}
            placeholder={inputPlaceholder}
            disabled={loading}
          />
          <IconButton
            color="primary"
            disabled={loading || sendDisabled}
            onClick={onSend}
            sx={{ bgcolor: 'primary.main', color: 'white', '&:hover': { bgcolor: 'primary.dark' }, '&:disabled': { bgcolor: 'action.disabledBackground' } }}
          >
            {loading ? <CircularProgress size={18} sx={{ color: 'white' }} /> : <Iconify icon="mdi:send" width={20} />}
          </IconButton>
        </Stack>
      </Stack>
    </Card>
  );
}

