import { useParams } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Chip from '@mui/material/Chip';
import Button from '@mui/material/Button';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardHeader from '@mui/material/CardHeader';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

type RuntimeRouteKind = 'text' | 'voice' | 'multimodal';

type TimelineEvent = {
  timestamp: string;
  stage: string;
  direction: string;
  payloadType: string;
  status: string;
  errorCode?: string;
  message?: string;
  correlationId: string;
};

type SessionSummary = {
  testSessionId: string;
  runtimeKind: string;
  status: string;
  correlationId: string;
  mode: string;
  agentId?: string;
  channelId?: string;
  threadId?: string;
  createdAt: string;
  updatedAt: string;
};

const runtimeMap: Record<RuntimeRouteKind, string> = {
  text: 'Text',
  voice: 'Voice',
  multimodal: 'MultimodalRealtime',
};

export default function RuntimeTestStudioPage() {
  const tenantId = useTenantId();
  const { runtimeKind = 'text' } = useParams();
  const routeRuntime = ((runtimeKind as RuntimeRouteKind) || 'text') as RuntimeRouteKind;
  const runtimeLabel = runtimeMap[routeRuntime] ?? runtimeMap.text;

  const [mode, setMode] = useState<'direct' | 'thread' | 'channel'>('direct');
  const [agentId, setAgentId] = useState('');
  const [channelId, setChannelId] = useState('');
  const [sessionId, setSessionId] = useState('');
  const [correlationId, setCorrelationId] = useState('');
  const [content, setContent] = useState('');
  const [attachmentName, setAttachmentName] = useState('');
  const [attachmentType, setAttachmentType] = useState('image/png');
  const [attachmentSize, setAttachmentSize] = useState('1024');
  const [attachmentRefs, setAttachmentRefs] = useState<string[]>([]);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [timeline, setTimeline] = useState<TimelineEvent[]>([]);
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [timelineStageFilter, setTimelineStageFilter] = useState('all');
  const [timelineStatusFilter, setTimelineStatusFilter] = useState('all');
  const [transcript, setTranscript] = useState<any>(null);
  const [metrics, setMetrics] = useState<any>(null);
  const [response, setResponse] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mapError = (raw: string | null) => {
    if (!raw) return null;
    if (raw.includes('session_rate_limited')) return 'Rate limit de sesión alcanzado. Espera unos segundos e intenta de nuevo.';
    if (raw.includes('runtime_incompatible')) return 'Runtime incompatible para esta operación.';
    if (raw.includes('attachment_not_supported')) return 'Adjunto no soportado para este flujo.';
    if (raw.includes('agent_required')) return 'Debes indicar un Agent ID para este modo.';
    return raw;
  };

  useEffect(() => {
    if (!sessionId || routeRuntime !== 'voice') return undefined;
    const handle = window.setInterval(() => {
      refreshTimeline(sessionId).catch(() => undefined);
    }, 3000);
    return () => window.clearInterval(handle);
  }, [sessionId, routeRuntime]);

  const limitations = useMemo(() => {
    if (routeRuntime === 'text') {
      return 'Text test supports direct/thread/channel. Attachment refs are accepted; OCR/extraction may be limited by runtime path.';
    }
    if (routeRuntime === 'voice') {
      return 'Voice test studio is integration-based in this phase (telephony callbacks), without browser softphone/WebRTC.';
    }
    return 'Multimodal test studio currently validates contract/timeline. Full bidirectional video/audio realtime transport is pending.';
  }, [routeRuntime]);

  const refreshTimeline = async (activeSessionId: string) => {
    const res = await axios.get(endpoints.agentflow.testStudio.timeline(tenantId, runtimeLabel, activeSessionId));
    setTimeline(res.data?.timelineEvents ?? []);
  };

  const loadSessions = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(endpoints.agentflow.testStudio.listSessions(tenantId, runtimeLabel));
      setSessions(res.data ?? []);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to load sessions');
    } finally {
      setLoading(false);
    }
  };

  const createSession = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(endpoints.agentflow.testStudio.createSession(tenantId, runtimeLabel), {
        mode,
        agentId: agentId || undefined,
        channelId: channelId || undefined,
        channelType: mode === 'channel' ? 'Api' : undefined,
        correlationId: correlationId || undefined,
      });
      const newSessionId = res.data?.testSessionId as string;
      setSessionId(newSessionId);
      if (res.data?.correlationId) setCorrelationId(res.data.correlationId);
      await refreshTimeline(newSessionId);
      setResponse(res.data);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to create test session');
    } finally {
      setLoading(false);
    }
  };

  const registerAttachment = async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(
        endpoints.agentflow.testStudio.registerAttachment(tenantId, runtimeLabel, sessionId),
        {
          name: attachmentName || 'attachment',
          contentType: attachmentType,
          sizeBytes: Number(attachmentSize || 0),
        }
      );
      const nextRefs = [...attachmentRefs, res.data?.attachmentRef].filter(Boolean);
      setAttachmentRefs(nextRefs);
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to register attachment');
    } finally {
      setLoading(false);
    }
  };

  const uploadAttachment = async () => {
    if (!sessionId || !selectedFile) return;
    setLoading(true);
    setError(null);
    try {
      const formData = new FormData();
      formData.append('file', selectedFile);
      const res = await axios.post(
        endpoints.agentflow.testStudio.uploadAttachment(tenantId, runtimeLabel, sessionId),
        formData,
        {
          headers: { 'Content-Type': 'multipart/form-data' },
        }
      );
      const nextRefs = [...attachmentRefs, res.data?.attachmentRef].filter(Boolean);
      setAttachmentRefs(nextRefs);
      setSelectedFile(null);
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to upload attachment');
    } finally {
      setLoading(false);
    }
  };

  const sendMessage = async () => {
    if (!sessionId || !content.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(endpoints.agentflow.testStudio.sendMessage(tenantId, runtimeLabel, sessionId), {
        content: content.trim(),
        attachmentRefs,
      });
      setResponse(res.data);
      setContent('');
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to send message');
    } finally {
      setLoading(false);
    }
  };

  const closeSession = async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      await axios.post(endpoints.agentflow.testStudio.close(tenantId, runtimeLabel, sessionId), {});
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to close session');
    } finally {
      setLoading(false);
    }
  };

  const loadTranscript = async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(endpoints.agentflow.testStudio.transcript(tenantId, runtimeLabel, sessionId));
      setTranscript(res.data);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to load transcript');
    } finally {
      setLoading(false);
    }
  };

  const loadMetrics = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(endpoints.agentflow.testStudio.metrics(tenantId, runtimeLabel));
      setMetrics(res.data);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to load metrics');
    } finally {
      setLoading(false);
    }
  };

  const linkCorrelation = async () => {
    if (!sessionId || !correlationId.trim()) return;
    setLoading(true);
    setError(null);
    try {
      await axios.patch(
        endpoints.agentflow.testStudio.updateCorrelation(tenantId, runtimeLabel, sessionId),
        { correlationId: correlationId.trim() }
      );
      await refreshTimeline(sessionId);
    } catch (err: any) {
      setError(mapError(err?.message) || 'Failed to link correlationId');
    } finally {
      setLoading(false);
    }
  };

  const filteredTimeline = timeline.filter((evt) => {
    if (timelineStageFilter !== 'all' && evt.stage !== timelineStageFilter) return false;
    if (timelineStatusFilter !== 'all' && evt.status !== timelineStatusFilter) return false;
    return true;
  });

  return (
    <>
      <Helmet>
        <title>Test Studio | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent>
        <Stack spacing={2}>
          <Typography variant="h4">Test Studio · {runtimeLabel}</Typography>
          <Alert severity="info">{limitations}</Alert>
          {error && <Alert severity="error">{error}</Alert>}
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, md: 6 }}>
              <Card>
                <CardHeader title="Session" subheader="Create a runtime test session" />
                <CardContent>
                  <Stack spacing={2}>
                    <Select value={mode} onChange={(e) => setMode(e.target.value as 'direct' | 'thread' | 'channel')}>
                      <MenuItem value="direct">direct</MenuItem>
                      <MenuItem value="thread">thread</MenuItem>
                      <MenuItem value="channel">channel</MenuItem>
                    </Select>
                    <TextField label="Agent ID (Text direct/thread)" value={agentId} onChange={(e) => setAgentId(e.target.value)} />
                    <TextField label="Channel ID (Text channel)" value={channelId} onChange={(e) => setChannelId(e.target.value)} />
                    <TextField
                      label="Correlation ID (Voice: CallSid)"
                      value={correlationId}
                      onChange={(e) => setCorrelationId(e.target.value)}
                      helperText="Set this before create session to link Twilio events to this timeline."
                    />
                    <Button variant="contained" onClick={createSession} disabled={loading}>
                      Create Session
                    </Button>
                    <Button variant="outlined" onClick={loadSessions} disabled={loading}>
                      Load Sessions
                    </Button>
                    <Button variant="outlined" onClick={linkCorrelation} disabled={loading || !sessionId || !correlationId.trim()}>
                      Link Correlation ID
                    </Button>
                    <TextField label="Session ID" value={sessionId} onChange={(e) => setSessionId(e.target.value)} />
                    <Select
                      value={sessionId}
                      onChange={(e) => setSessionId(e.target.value)}
                      displayEmpty
                    >
                      <MenuItem value="">Select existing session</MenuItem>
                      {sessions.map((s) => (
                        <MenuItem key={s.testSessionId} value={s.testSessionId}>
                          {s.testSessionId} · {s.status} · {s.mode}
                        </MenuItem>
                      ))}
                    </Select>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <Card>
                <CardHeader title="Message" subheader="Send input and observe timeline" />
                <CardContent>
                  <Stack spacing={2}>
                    <TextField label="Message" multiline minRows={3} value={content} onChange={(e) => setContent(e.target.value)} />
                    <Button variant="contained" onClick={sendMessage} disabled={loading || !sessionId}>
                      Send
                    </Button>
                    <Button variant="outlined" onClick={closeSession} disabled={loading || !sessionId}>
                      Close Session
                    </Button>
                    <Button variant="outlined" onClick={loadTranscript} disabled={loading || !sessionId}>
                      Load Transcript
                    </Button>
                    <Button variant="outlined" onClick={loadMetrics} disabled={loading}>
                      Load Runtime Metrics
                    </Button>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <Card>
                <CardHeader title="Attachments (metadata refs)" />
                <CardContent>
                  <Stack spacing={2}>
                    <TextField label="Name" value={attachmentName} onChange={(e) => setAttachmentName(e.target.value)} />
                    <TextField label="Content Type" value={attachmentType} onChange={(e) => setAttachmentType(e.target.value)} />
                    <TextField label="Size Bytes" value={attachmentSize} onChange={(e) => setAttachmentSize(e.target.value)} />
                    <Button variant="outlined" onClick={registerAttachment} disabled={loading || !sessionId}>
                      Register Attachment
                    </Button>
                    <Button variant="outlined" component="label" disabled={loading || !sessionId}>
                      Select File
                      <input
                        type="file"
                        hidden
                        onChange={(e) => setSelectedFile(e.target.files?.[0] ?? null)}
                      />
                    </Button>
                    {selectedFile && (
                      <Typography variant="caption">
                        selected: {selectedFile.name} ({selectedFile.type || 'unknown'}, {selectedFile.size} bytes)
                      </Typography>
                    )}
                    <Button variant="contained" onClick={uploadAttachment} disabled={loading || !sessionId || !selectedFile}>
                      Upload File
                    </Button>
                    <Typography variant="body2">attachmentRefs: {attachmentRefs.join(', ') || '-'}</Typography>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <Card>
                <CardHeader title="Last Response" />
                <CardContent>
                  <Box component="pre" sx={{ whiteSpace: 'pre-wrap', m: 0 }}>
                    {JSON.stringify(response, null, 2)}
                  </Box>
                </CardContent>
              </Card>
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <Card>
                <CardHeader title="Transcript" />
                <CardContent>
                  {!!transcript?.entries?.length ? (
                    <Stack spacing={1}>
                      {transcript.entries.map((entry: any, idx: number) => (
                        <Box key={`${entry.timestamp}-${idx}`}>
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Chip size="small" label={entry.speaker} />
                            <Typography variant="caption">
                              {new Date(entry.timestamp).toLocaleString()} · {entry.stage} · {entry.status}
                            </Typography>
                          </Stack>
                          <Typography variant="body2">{entry.text}</Typography>
                          <Divider sx={{ mt: 1 }} />
                        </Box>
                      ))}
                    </Stack>
                  ) : (
                    <Typography variant="body2">No transcript entries loaded.</Typography>
                  )}
                </CardContent>
              </Card>
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <Card>
                <CardHeader title="Runtime Metrics" />
                <CardContent>
                  {metrics ? (
                    <Stack spacing={1}>
                      <Typography variant="body2">Runtime: {metrics.runtimeKind}</Typography>
                      <Typography variant="body2">Total sessions: {metrics.totalSessions}</Typography>
                      <Typography variant="body2">Active sessions: {metrics.activeSessions}</Typography>
                      <Typography variant="body2">Completed sessions: {metrics.completedSessions}</Typography>
                      <Typography variant="body2">Success rate: {metrics.successRatePercent}%</Typography>
                      <Typography variant="body2">Avg e2e latency: {metrics.avgE2eLatencyMs} ms</Typography>
                      <Typography variant="body2">Error events: {metrics.totalErrorEvents}</Typography>
                    </Stack>
                  ) : (
                    <Typography variant="body2">No metrics loaded.</Typography>
                  )}
                </CardContent>
              </Card>
            </Grid>
          </Grid>
          <Card>
            <CardHeader title="Timeline" />
            <CardContent>
              <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
                <Select value={timelineStageFilter} onChange={(e) => setTimelineStageFilter(e.target.value)} size="small">
                  <MenuItem value="all">All stages</MenuItem>
                  {[...new Set(timeline.map((x) => x.stage))].map((stage) => (
                    <MenuItem key={stage} value={stage}>{stage}</MenuItem>
                  ))}
                </Select>
                <Select value={timelineStatusFilter} onChange={(e) => setTimelineStatusFilter(e.target.value)} size="small">
                  <MenuItem value="all">All status</MenuItem>
                  {[...new Set(timeline.map((x) => x.status))].map((status) => (
                    <MenuItem key={status} value={status}>{status}</MenuItem>
                  ))}
                </Select>
              </Stack>
              <Stack spacing={1}>
                {filteredTimeline.map((evt, index) => (
                  <Box key={`${evt.timestamp}-${index}`}>
                    <Typography variant="body2">
                      [{new Date(evt.timestamp).toLocaleString()}] {evt.stage} · {evt.status} · {evt.direction} · {evt.payloadType}
                    </Typography>
                    {evt.message && <Typography variant="caption">{mapError(evt.message) ?? evt.message}</Typography>}
                    <Divider sx={{ mt: 1 }} />
                  </Box>
                ))}
                {!filteredTimeline.length && <Typography variant="body2">No events for current filters.</Typography>}
              </Stack>
            </CardContent>
          </Card>
        </Stack>
      </DashboardContent>
    </>
  );
}
