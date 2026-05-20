import dayjs from 'dayjs';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import { Iconify } from 'src/components/iconify';

import type { InboxConversation } from './types';

// ----------------------------------------------------------------------

interface InboxTableProps {
  conversations: InboxConversation[];
  loading?: boolean;
  onView: (conversation: InboxConversation) => void;
  onReassign: (conversationId: string) => void;
  onResolve: (conversationId: string) => void;
}

const STATE_BY_INDEX: Record<number, string> = {
  0: 'AwaitingClassification',
  1: 'Classified',
  2: 'LowConfidence',
  3: 'NoMatch',
  4: 'InProgress',
  5: 'PendingHumanReview',
  6: 'Resolved',
  7: 'Escalated',
  8: 'Abandoned',
  9: 'ConflictDetected',
};

const CONFIDENCE_BY_INDEX: Record<number, string> = {
  0: 'NoMatch',
  1: 'Low',
  2: 'Medium',
  3: 'High',
};

const normalizeState = (state: unknown): string => {
  if (typeof state === 'string') return state;
  if (typeof state === 'number') return STATE_BY_INDEX[state] ?? `State${state}`;
  return 'Unknown';
};

const normalizeConfidence = (confidence: unknown): string => {
  if (typeof confidence === 'string') return confidence;
  if (typeof confidence === 'number') return CONFIDENCE_BY_INDEX[confidence] ?? `Level${confidence}`;
  return 'Unknown';
};

const stateColor = (state: string) => {
  switch (state) {
    case 'AwaitingClassification': return 'warning';
    case 'Classified': return 'info';
    case 'LowConfidence': return 'warning';
    case 'NoMatch': return 'error';
    case 'InProgress': return 'primary';
    case 'PendingHumanReview': return 'warning';
    case 'Resolved': return 'success';
    case 'Escalated': return 'error';
    case 'ConflictDetected': return 'error';
    case 'Abandoned': return 'error';
    default: return 'default';
  }
};

const confidenceColor = (confidence: string) => {
  switch (confidence) {
    case 'High': return 'success';
    case 'Medium': return 'warning';
    case 'Low': return 'error';
    default: return 'default';
  }
};

export function InboxTable({ conversations, loading, onView, onReassign, onResolve }: InboxTableProps) {
  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight={400}>
        <CircularProgress />
      </Box>
    );
  }

  if (conversations.length === 0) {
    return (
      <Card sx={{ p: 5, textAlign: 'center' }}>
        <Iconify icon="eva:inbox-outline" width={64} sx={{ mx: 'auto', mb: 2, color: 'text.disabled' }} />
        <Typography variant="h6" color="text.secondary">
          No conversations found
        </Typography>
        <Typography variant="body2" color="text.disabled" sx={{ mt: 1 }}>
          Conversations will appear here as they come in
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
              <TableCell>User / Channel</TableCell>
              <TableCell>Last Message</TableCell>
              <TableCell>State</TableCell>
              <TableCell>Intent</TableCell>
              <TableCell>Confidence</TableCell>
              <TableCell>Created</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {conversations.map((conv) => {
              const state = normalizeState((conv as any).state);
              const confidence = normalizeConfidence((conv as any).confidence);
              return (
              <TableRow
                key={conv.id} 
                hover
                sx={{
                  bgcolor: conv.requires_human_review ? 'warning.lighter' : 'inherit',
                }}
              >
                <TableCell>
                  <Stack spacing={0.5}>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Typography variant="subtitle2">{conv.user_identifier}</Typography>
                      {conv.requires_human_review && (
                        <Iconify icon="eva:alert-triangle-fill" color="warning.main" width={16} />
                      )}
                    </Stack>
                    <Chip 
                      label={conv.channel} 
                      size="small" 
                      variant="outlined"
                      sx={{ width: 'fit-content' }}
                    />
                  </Stack>
                </TableCell>
                <TableCell>
                  <Typography 
                    variant="body2" 
                    color="text.secondary"
                    sx={{
                      maxWidth: 300,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {conv.last_message}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Chip 
                    label={state.replace(/([A-Z])/g, ' $1').trim()}
                    size="small"
                    color={stateColor(state) as any}
                  />
                </TableCell>
                <TableCell>
                  {conv.detected_intent_key ? (
                    <Typography variant="body2">{conv.detected_intent_key}</Typography>
                  ) : (
                    <Typography variant="body2" color="text.disabled">
                      —
                    </Typography>
                  )}
                </TableCell>
                <TableCell>
                  <Chip
                    label={confidence}
                    size="small"
                    color={confidenceColor(confidence) as any}
                  />
                </TableCell>
                <TableCell>
                  <Typography variant="caption" color="text.secondary">
                    {dayjs(conv.created_at).format('MMM DD, HH:mm')}
                  </Typography>
                </TableCell>
                <TableCell align="right">
                  <IconButton size="small" onClick={() => onView(conv)}>
                    <Iconify icon="eva:eye-outline" />
                  </IconButton>
                  <IconButton 
                    size="small" 
                    onClick={() => onReassign(conv.id)}
                    disabled={state === 'Resolved'}
                  >
                    <Iconify icon="eva:shuffle-2-outline" />
                  </IconButton>
                  <IconButton 
                    size="small" 
                    onClick={() => onResolve(conv.id)}
                    disabled={state === 'Resolved'}
                  >
                    <Iconify icon="eva:checkmark-outline" />
                  </IconButton>
                </TableCell>
              </TableRow>
            )})}
          </TableBody>
        </Table>
      </TableContainer>
    </Card>
  );
}
