import axios from 'axios';
import React, { useRef, useState, useEffect } from 'react';

import {
  Box,
  Chip,
  Paper,
  Alert,
  Stack,
  Divider,
  TextField,
  IconButton,
  Typography,
  CircularProgress,
} from '@mui/material';

import { Iconify } from 'src/components/iconify';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
}

interface ChatInterfaceProps {
  agentId: string;
  agentName: string;
  tenantId: string;
}

interface ThreadInfo {
  threadId: string;
  threadKey: string;
  turnCount: number;
  totalTokens: number;
}

export function ChatInterface({ agentId, agentName, tenantId }: ChatInterfaceProps) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [threadInfo, setThreadInfo] = useState<ThreadInfo | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom when new messages arrive
  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  // Initialize thread on mount
  useEffect(() => {
    const initializeThread = async () => {
      try {
        const response = await axios.post(
          `/api/v1/tenants/${tenantId}/threads`,
          {
            agentId,
            expiresIn: '02:00:00', // 2 hours
          }
        );

        console.log('Thread created:', response.data);
        setThreadInfo({
          threadId: response.data.threadId,
          threadKey: response.data.threadKey,
          turnCount: 0,
          totalTokens: 0,
        });
      } catch (err: any) {
        console.error('Failed to create thread:', err);
        setError('Failed to initialize conversation');
      }
    };

    initializeThread();
  }, [agentId, tenantId]);

  const sendMessage = async () => {
    if (!input.trim() || !threadInfo) return;

    const userMessage: Message = {
      role: 'user',
      content: input.trim(),
      timestamp: new Date(),
    };

    setMessages((prev) => [...prev, userMessage]);
    setInput('');
    setLoading(true);
    setError(null);

    try {
      const response = await axios.post(
        `/api/v1/tenants/${tenantId}/threads/${threadInfo.threadId}/messages`,
        {
          message: userMessage.content,
        }
      );

      const assistantMessage: Message = {
        role: 'assistant',
        content: response.data.assistantResponse,
        timestamp: new Date(),
      };

      setMessages((prev) => [...prev, assistantMessage]);

      // Update thread info
      setThreadInfo({
        ...threadInfo,
        turnCount: response.data.totalTurns,
        totalTokens: threadInfo.totalTokens + response.data.tokensUsed,
      });

      console.log('Execution:', response.data.executionId, 'Tokens:', response.data.tokensUsed);
    } catch (err: any) {
      console.error('Failed to send message:', err);
      setError(err.response?.data?.error?.message || 'Failed to send message');
    } finally {
      setLoading(false);
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  };

  return (
    <Paper
      elevation={2}
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
      }}
    >
      {/* Header */}
      <Box
        sx={{
          p: 2,
          borderBottom: 1,
          borderColor: 'divider',
          bgcolor: 'primary.main',
          color: 'primary.contrastText',
        }}
      >
        <Stack direction="row" alignItems="center" spacing={2}>
          <Iconify icon="mdi:robot-outline" />
          <Box flex={1}>
            <Typography variant="h6">{agentName}</Typography>
            {threadInfo && (
              <Typography variant="caption">
                {threadInfo.turnCount} turns • {threadInfo.totalTokens.toLocaleString()} tokens
              </Typography>
            )}
          </Box>
          {threadInfo && (
            <Chip
              label={`Thread: ${threadInfo.threadKey.slice(0, 16)}...`}
              size="small"
              variant="outlined"
              sx={{ color: 'inherit', borderColor: 'inherit' }}
            />
          )}
        </Stack>
      </Box>

      {/* Messages */}
      <Box
        sx={{
          flex: 1,
          overflowY: 'auto',
          p: 2,
          bgcolor: 'grey.50',
        }}
      >
        {messages.length === 0 && !loading && (
          <Box
            sx={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              height: '100%',
              color: 'text.secondary',
            }}
          >
            <Iconify icon="mdi:robot-outline" sx={{ fontSize: 64, mb: 2, opacity: 0.3 }} />
            <Typography variant="h6">Start a conversation</Typography>
            <Typography variant="body2">Ask me anything!</Typography>
          </Box>
        )}

        {messages.map((msg, idx) => (
          <Box
            key={idx}
            sx={{
              mb: 2,
              display: 'flex',
              flexDirection: msg.role === 'user' ? 'row-reverse' : 'row',
              alignItems: 'flex-start',
            }}
          >
            <Box
              sx={{
                bgcolor: msg.role === 'user' ? 'primary.main' : 'background.paper',
                color: msg.role === 'user' ? 'primary.contrastText' : 'text.primary',
                p: 1.5,
                borderRadius: 2,
                maxWidth: '70%',
                boxShadow: 1,
                ml: msg.role === 'assistant' ? 1 : 0,
                mr: msg.role === 'user' ? 1 : 0,
              }}
            >
              <Stack direction="row" spacing={1} alignItems="center" mb={0.5}>
                {msg.role === 'user' ? (
                  <Iconify icon="mdi:account-circle" width={20} />
                ) : (
                  <Iconify icon="mdi:robot-outline" width={20} />
                )}
                <Typography variant="caption" fontWeight="bold">
                  {msg.role === 'user' ? 'You' : agentName}
                </Typography>
              </Stack>
              <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                {msg.content}
              </Typography>
              <Typography variant="caption" sx={{ opacity: 0.7, mt: 0.5, display: 'block' }}>
                {msg.timestamp.toLocaleTimeString()}
              </Typography>
            </Box>
          </Box>
        ))}

        {loading && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <CircularProgress size={20} />
            <Typography variant="body2" color="text.secondary">
              {agentName} is thinking...
            </Typography>
          </Box>
        )}

        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}

        <div ref={messagesEndRef} />
      </Box>

      <Divider />

      {/* Input */}
      <Box sx={{ p: 2, bgcolor: 'background.paper' }}>
        <Stack direction="row" spacing={1}>
          <TextField
            fullWidth
            multiline
            maxRows={4}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyPress={handleKeyPress}
            placeholder="Type your message..."
            disabled={loading || !threadInfo}
            variant="outlined"
            size="small"
          />
          <IconButton
            color="primary"
            onClick={sendMessage}
            disabled={!input.trim() || loading || !threadInfo}
            sx={{
              bgcolor: 'primary.main',
              color: 'primary.contrastText',
              '&:hover': {
                bgcolor: 'primary.dark',
              },
              '&:disabled': {
                bgcolor: 'action.disabledBackground',
              },
            }}
          >
            <Iconify icon="mdi:send" />
          </IconButton>
        </Stack>
      </Box>
    </Paper>
  );
}
