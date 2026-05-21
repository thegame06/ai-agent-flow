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
  actor?: string;
  deliveryState?: string;
  errorMessage?: string;
  metadata?: Record<string, string>;
};

type Props = {
  messages: SessionMessage[];
  loading: boolean;
  hasMore: boolean;
  onLoadMore: () => void;
  resolveAgentName?: (agentId: string) => string | undefined;
};

const WINDOW_SIZE = 80;

export function MessageTimeline({ messages, loading, hasMore, onLoadMore, resolveAgentName }: Props) {
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
              const actor = message.actor || (isIncoming ? 'customer' : 'agent');
              const metadata = message.metadata || {};
              const eventType = metadata.event_type || '';
              const isSystem = actor === 'billing' || actor === 'system' || metadata.actor === 'system';
              const hasFailure = message.deliveryState === 'not_sent' || Boolean(message.errorMessage);
              const isInternalOnly = message.deliveryState === 'suppressed' || metadata['agentflow.visibility'] === 'inbox_only';
              const actorLabel = (() => {
                if (isIncoming) return 'Cliente';
                if (isSystem && eventType === 'workflow_handoff') return 'Sistema (enrutamiento)';
                if (isSystem) return 'Sistema';
                if (actor.startsWith('agent:')) {
                  const agentId = actor.slice(6);
                  const agentName = resolveAgentName?.(agentId);
                  return agentName ? `Agente ${agentName}` : `Agente ${agentId}`;
                }
                if (actor === 'bot') return 'Agente';
                return actor;
              })();
              const bubbleBg = isIncoming
                ? 'background.paper'
                : isSystem
                  ? 'warning.lighter'
                  : hasFailure
                    ? 'error.lighter'
                    : actor === 'bot'
                      ? 'primary.main'
                      : 'success.main';
              const bubbleColor = isIncoming
                ? 'text.primary'
                : isSystem
                  ? 'warning.darker'
                  : hasFailure
                    ? 'error.darker'
                    : 'common.white';

              return (
                <Stack key={message.id} alignItems={isIncoming ? 'flex-start' : 'flex-end'}>
                  <Box
                    sx={{
                      maxWidth: '82%',
                      px: 1.5,
                      py: 1.1,
                      borderRadius: 2,
                      bgcolor: bubbleBg,
                      color: bubbleColor,
                      boxShadow: 1,
                    }}
                  >
                    <Typography variant="caption" sx={{ display: 'block', mb: 0.5, opacity: 0.72, textTransform: 'capitalize' }}>
                      {actorLabel}
                    </Typography>
                    {eventType === 'workflow_handoff' && (
                      <Typography variant="caption" sx={{ display: 'block', mb: 0.6, fontWeight: 700, opacity: 0.85 }}>
                        Workflow asignado
                      </Typography>
                    )}
                    {isInternalOnly && (
                      <Typography variant="caption" sx={{ display: 'block', mb: 0.6, fontWeight: 700, opacity: 0.85 }}>
                        Mensaje interno (no enviado al cliente)
                      </Typography>
                    )}
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                      {message.content}
                    </Typography>
                    <Typography variant="caption" sx={{ display: 'block', mt: 0.75, opacity: 0.72 }}>
                      {new Date(message.createdAt).toLocaleTimeString()}
                      {message.deliveryState ? ` | ${message.deliveryState}` : ''}
                    </Typography>
                    {message.errorMessage && (
                      <Typography variant="caption" sx={{ display: 'block', mt: 0.4, color: 'error.main', opacity: 0.92 }}>
                        {message.errorMessage}
                      </Typography>
                    )}
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
