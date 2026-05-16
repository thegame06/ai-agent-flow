import { useMemo, useState } from 'react';

import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';

type SessionMessage = {
  id: string;
  direction: string;
  content: string;
  createdAt: string;
};

type Props = {
  messages: SessionMessage[];
  loading: boolean;
  hasMore: boolean;
  onLoadMore: () => void;
};

const WINDOW_SIZE = 80;

export function MessageTimeline({ messages, loading, hasMore, onLoadMore }: Props) {
  const [windowEnd, setWindowEnd] = useState(WINDOW_SIZE);

  const visibleMessages = useMemo(() => {
    const count = Math.max(WINDOW_SIZE, windowEnd);
    return messages.slice(Math.max(0, messages.length - count));
  }, [messages, windowEnd]);

  return (
    <Box sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2, overflow: 'hidden', bgcolor: 'background.neutral' }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ px: 1.5, py: 1, borderBottom: '1px solid', borderColor: 'divider', bgcolor: 'background.paper' }}>
        <Button size="small" variant="text" disabled={!hasMore || loading} onClick={onLoadMore}>
          Cargar anteriores
        </Button>
        <Typography variant="caption" color="text.secondary">
          {messages.length} mensajes
        </Typography>
      </Stack>

      <Box sx={{ height: 320, overflowY: 'auto', p: 1.5 }}>
        {loading && !messages.length ? (
          <Stack alignItems="center" justifyContent="center" sx={{ height: '100%' }}>
            <CircularProgress size={24} />
          </Stack>
        ) : (
          <Stack spacing={1.25}>
            {visibleMessages.map((message) => {
              const isIncoming = message.direction === 'Incoming';
              return (
                <Stack key={message.id} alignItems={isIncoming ? 'flex-start' : 'flex-end'}>
                  <Box
                    sx={{
                      maxWidth: '82%',
                      px: 1.5,
                      py: 1.1,
                      borderRadius: 2,
                      bgcolor: isIncoming ? 'background.paper' : 'primary.main',
                      color: isIncoming ? 'text.primary' : 'primary.contrastText',
                      boxShadow: 1,
                    }}
                  >
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                      {message.content}
                    </Typography>
                    <Typography variant="caption" sx={{ display: 'block', mt: 0.75, opacity: 0.72 }}>
                      {new Date(message.createdAt).toLocaleTimeString()}
                    </Typography>
                  </Box>
                </Stack>
              );
            })}
            {messages.length > WINDOW_SIZE && visibleMessages.length < messages.length && (
              <Button size="small" variant="text" onClick={() => setWindowEnd((prev) => prev + WINDOW_SIZE)}>
                Mostrar mas del buffer cargado
              </Button>
            )}
          </Stack>
        )}
      </Box>
    </Box>
  );
}
