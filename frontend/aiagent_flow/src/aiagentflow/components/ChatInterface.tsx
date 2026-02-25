import React, { useRef, useState, useEffect, useCallback } from 'react';

import {
  Box,
  Chip,
  Paper,
  Alert,
  Stack,
  Divider,
  Select,
  Button,
  MenuItem,
  TextField,
  IconButton,
  Typography,
  CircularProgress,
} from '@mui/material';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
  executionId?: string;
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

interface ThreadSummary {
  threadId: string;
  threadKey: string;
  turnCount: number;
}

interface ExecutionSummary {
  id: string;
  status: string;
  createdAt: string;
  totalTokensUsed?: number;
  totalSteps?: number;
}

interface ExecutionDetail {
  id: string;
  status?: string;
  errorMessage?: string;
  output?: {
    finalResponse?: string;
    totalTokensUsed?: number;
  };
}

export function ChatInterface({ agentId, agentName, tenantId }: ChatInterfaceProps) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [threadInfo, setThreadInfo] = useState<ThreadInfo | null>(null);
  const [threads, setThreads] = useState<ThreadSummary[]>([]);
  const [executions, setExecutions] = useState<ExecutionSummary[]>([]);
  const [selectedExecution, setSelectedExecution] = useState<ExecutionDetail | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const activeThreadStorageKey = `af:active-thread:${tenantId}:${agentId}`;

  // Auto-scroll to bottom when new messages arrive
  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const mapHistoryToMessages = useCallback((turns: any[]): Message[] => {
    const nextMessages: Message[] = [];
    turns.forEach((turn) => {
      if (turn.userMessage) {
        nextMessages.push({
          role: 'user',
          content: turn.userMessage,
          timestamp: new Date(turn.timestamp),
        });
      }
      if (turn.assistantResponse) {
        nextMessages.push({
          role: 'assistant',
          content: turn.assistantResponse,
          timestamp: new Date(turn.timestamp),
        });
      }
    });
    return nextMessages;
  }, []);

  const createThread = useCallback(async () => {
    const response = await axios.post(`/api/v1/tenants/${tenantId}/threads`, {
      agentId,
      expiresIn: '02:00:00',
    });

    const created = {
      threadId: response.data.threadId,
      threadKey: response.data.threadKey,
      turnCount: response.data.turnCount ?? 0,
      totalTokens: 0,
    };

    localStorage.setItem(activeThreadStorageKey, created.threadId);
    setThreadInfo(created);
    setThreads((prev) => [
      { threadId: created.threadId, threadKey: created.threadKey, turnCount: created.turnCount },
      ...prev.filter((t) => t.threadId !== created.threadId),
    ]);
    setMessages([]);
  }, [activeThreadStorageKey, agentId, tenantId]);

  const loadThreadHistory = useCallback(async (threadId: string) => {
    const historyResponse = await axios.get(`/api/v1/tenants/${tenantId}/threads/${threadId}/history`);
    const turns = historyResponse.data?.turns ?? [];
    setMessages(mapHistoryToMessages(turns));
    setThreadInfo((prev) =>
      prev
        ? { ...prev, turnCount: historyResponse.data?.totalTurns ?? prev.turnCount, totalTokens: historyResponse.data?.tokenStats?.totalTokens ?? prev.totalTokens }
        : prev
    );
  }, [mapHistoryToMessages, tenantId]);

  const loadExecutions = useCallback(async (threadId?: string) => {
    try {
      const qs = threadId ? `&threadId=${encodeURIComponent(threadId)}` : '';
      const res = await axios.get(`/api/v1/tenants/${tenantId}/agents/${agentId}/executions?limit=20${qs}`);
      setExecutions((res.data ?? []) as ExecutionSummary[]);
    } catch (err) {
      console.warn('Failed to load executions', err);
    }
  }, [agentId, tenantId]);

  const loadExecutionDetail = useCallback(async (executionId: string) => {
    try {
      const res = await axios.get(`/api/v1/tenants/${tenantId}/executions/${executionId}`);
      setSelectedExecution(res.data as ExecutionDetail);
    } catch (err) {
      console.warn('Failed to load execution detail', err);
    }
  }, [tenantId]);

  useEffect(() => {
    const initializeThread = async () => {
      try {
        setError(null);
        const listResponse = await axios.get(`/api/v1/tenants/${tenantId}/threads?agentId=${agentId}`);
        const existingThreads = (listResponse.data ?? []) as ThreadSummary[];
        setThreads(existingThreads);

        const storedThreadId = localStorage.getItem(activeThreadStorageKey);
        const preferred = existingThreads.find((t) => t.threadId === storedThreadId);
        const current = preferred ?? existingThreads[0];

        if (current) {
          localStorage.setItem(activeThreadStorageKey, current.threadId);
          setThreadInfo({
            threadId: current.threadId,
            threadKey: current.threadKey,
            turnCount: current.turnCount ?? 0,
            totalTokens: 0,
          });
          await loadThreadHistory(current.threadId);
          await loadExecutions(current.threadId);
          return;
        }

        await createThread();
        const newThreadId = localStorage.getItem(activeThreadStorageKey) ?? undefined;
        await loadExecutions(newThreadId);
      } catch (err: any) {
        console.error('Failed to initialize thread:', err);
        setError('Failed to initialize conversation');
      }
    };

    initializeThread();
  }, [activeThreadStorageKey, agentId, createThread, loadExecutions, loadThreadHistory, tenantId]);

  useEffect(() => {
    if (!threadInfo?.threadId) return;

    const id = window.setInterval(async () => {
      await loadExecutions(threadInfo.threadId);
    }, 8000);

    return () => window.clearInterval(id);
  }, [loadExecutions, threadInfo?.threadId]);

  useEffect(() => {
    const id = window.setInterval(async () => {
      try {
        const listResponse = await axios.get(`/api/v1/tenants/${tenantId}/threads?agentId=${agentId}`);
        setThreads((listResponse.data ?? []) as ThreadSummary[]);
      } catch {
        // ignore periodic thread refresh errors
      }
    }, 20000);

    return () => window.clearInterval(id);
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
        executionId: response.data.executionId,
      };

      setMessages((prev) => [...prev, assistantMessage]);

      // Update thread info
      setThreadInfo({
        ...threadInfo,
        turnCount: response.data.totalTurns,
        totalTokens: threadInfo.totalTokens + response.data.tokensUsed,
      });

      console.log('Execution:', response.data.executionId, 'Tokens:', response.data.tokensUsed);
      await loadExecutions(threadInfo.threadId);
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
                {threadInfo.turnCount} turns • {threadInfo.totalTokens.toLocaleString()} tokens • {executions.length} execs
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

          {threads.length > 0 && (
            <Select
              size="small"
              value={threadInfo?.threadId ?? ''}
              onChange={async (e) => {
                const selectedThreadId = String(e.target.value);
                const selected = threads.find((t) => t.threadId === selectedThreadId);
                if (!selected) return;
                localStorage.setItem(activeThreadStorageKey, selected.threadId);
                setThreadInfo((prev) => ({
                  threadId: selected.threadId,
                  threadKey: selected.threadKey,
                  turnCount: selected.turnCount ?? prev?.turnCount ?? 0,
                  totalTokens: prev?.totalTokens ?? 0,
                }));
                await loadThreadHistory(selected.threadId);
                await loadExecutions(selected.threadId);
              }}
              sx={{ minWidth: 170, bgcolor: 'background.paper' }}
            >
              {threads.map((t) => (
                <MenuItem key={t.threadId} value={t.threadId}>
                  {t.threadKey.slice(0, 20)}
                </MenuItem>
              ))}
            </Select>
          )}
          <Button
            size="small"
            variant="outlined"
            color="inherit"
            onClick={async () => {
              try {
                const listResponse = await axios.get(`/api/v1/tenants/${tenantId}/threads?agentId=${agentId}`);
                setThreads((listResponse.data ?? []) as ThreadSummary[]);
              } catch {
                setError('Failed to refresh threads');
              }
            }}
          >
            Refresh
          </Button>
          <IconButton
            color="inherit"
            onClick={async () => {
              try {
                setLoading(true);
                await createThread();
                const newThreadId = localStorage.getItem(activeThreadStorageKey) ?? undefined;
                await loadExecutions(newThreadId);
              } catch (err: any) {
                setError(err?.message || 'Failed to start a new thread');
              } finally {
                setLoading(false);
              }
            }}
          >
            <Iconify icon="mdi:plus-circle-outline" />
          </IconButton>
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
        {threads.length > 0 && (
          <Box sx={{ mb: 2 }}>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
              Conversation threads
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              {threads.slice(0, 10).map((t) => (
                <Chip
                  key={t.threadId}
                  size="small"
                  color={t.threadId === threadInfo?.threadId ? 'primary' : 'default'}
                  label={`${t.threadKey.slice(0, 16)} • ${t.turnCount ?? 0}`}
                  variant={t.threadId === threadInfo?.threadId ? 'filled' : 'outlined'}
                  onClick={async () => {
                    localStorage.setItem(activeThreadStorageKey, t.threadId);
                    setThreadInfo((prev) => ({
                      threadId: t.threadId,
                      threadKey: t.threadKey,
                      turnCount: t.turnCount ?? prev?.turnCount ?? 0,
                      totalTokens: prev?.totalTokens ?? 0,
                    }));
                    await loadThreadHistory(t.threadId);
                    await loadExecutions(t.threadId);
                  }}
                  clickable
                />
              ))}
            </Stack>
          </Box>
        )}

        {executions.length > 0 && (
          <Box sx={{ mb: 2 }}>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
              Recent executions
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              {executions.slice(0, 6).map((e) => (
                <Chip
                  key={e.id}
                  size="small"
                  label={`${e.status} • ${e.id.slice(0, 8)}`}
                  variant="outlined"
                  onClick={() => loadExecutionDetail(e.id)}
                  clickable
                />
              ))}
            </Stack>
          </Box>
        )}

        {selectedExecution && (
          <Alert severity={selectedExecution.status === 'Failed' ? 'error' : 'info'} sx={{ mb: 2 }}>
            <Typography variant="subtitle2">Execution {selectedExecution.id.slice(0, 8)}</Typography>
            <Typography variant="body2" sx={{ mt: 0.5, mb: 1 }}>
              {selectedExecution.output?.finalResponse || selectedExecution.errorMessage || 'No detail available'}
            </Typography>
            <Stack direction="row" spacing={1}>
              <Button
                size="small"
                variant="outlined"
                onClick={() => {
                  const text = selectedExecution.output?.finalResponse;
                  if (text) {
                    setInput((prev) => (prev ? `${prev}\n\nContexto previo:\n${text}` : `Contexto previo:\n${text}`));
                  }
                }}
              >
                Usar como contexto
              </Button>
              <Button size="small" onClick={() => setSelectedExecution(null)}>
                Cerrar
              </Button>
            </Stack>
          </Alert>
        )}
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
                {msg.executionId ? ` • exec ${msg.executionId.slice(0, 8)}` : ''}
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
