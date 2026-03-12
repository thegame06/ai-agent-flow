import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TableRow from '@mui/material/TableRow';
import Snackbar from '@mui/material/Snackbar';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import { alpha, useTheme } from '@mui/material/styles';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';
import TableContainer from '@mui/material/TableContainer';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';

const severityColor = (severity: string) => {
  switch (severity) {
    case 'critical': return 'error';
    case 'error': return 'error';
    case 'warning': return 'warning';
    case 'success': return 'success';
    default: return 'info';
  }
};

export default function AuditPage() {
  const theme = useTheme();
  const tenantId = useTenantId();
  const [logs, setLogs] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [correlationId, setCorrelationId] = useState('');
  const [action, setAction] = useState('');
  const [limit, setLimit] = useState(150);
  const [correlations, setCorrelations] = useState<any[]>([]);
  const [selectedCorrelation, setSelectedCorrelation] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  const selectedTimeline = logs
    .filter((x) => x.correlationId === selectedCorrelation)
    .slice()
    .sort((a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime());

  const parseJson = (raw: string | null | undefined) => {
    if (!raw) return null;
    try {
      return JSON.parse(raw) as Record<string, unknown>;
    } catch {
      return null;
    }
  };

  const formatJson = (raw: string | null | undefined) => {
    if (!raw) return '-';
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  };

  const computeDiff = (prevRaw: string | null | undefined, nextRaw: string | null | undefined) => {
    const prev = parseJson(prevRaw) ?? {};
    const next = parseJson(nextRaw) ?? {};

    const keys = Array.from(new Set([...Object.keys(prev), ...Object.keys(next)])).sort();
    const changes = keys
      .filter((k) => JSON.stringify(prev[k]) !== JSON.stringify(next[k]))
      .map((k) => {
        const hasPrev = Object.prototype.hasOwnProperty.call(prev, k);
        const hasNext = Object.prototype.hasOwnProperty.call(next, k);
        const changeType = !hasPrev && hasNext ? 'added' : hasPrev && !hasNext ? 'removed' : 'changed';

        return { key: k, from: prev[k], to: next[k], changeType };
      });

    return changes;
  };

  const copyToClipboard = async (text: string, label: string) => {
    await navigator.clipboard.writeText(text);
    setCopied(label);
  };

  const fetchLogs = useCallback(async () => {
    try {
      setLoading(true);
      const params = new URLSearchParams();
      params.set('limit', String(limit));
      if (correlationId.trim()) params.set('correlationId', correlationId.trim());
      if (action.trim()) params.set('action', action.trim());

      const [response, corrResponse] = await Promise.all([
        axios.get(`${endpoints.agentflow.audit.list(tenantId)}?${params.toString()}`),
        axios.get(`${endpoints.agentflow.audit.correlations(tenantId)}?limit=30`),
      ]);
      setLogs(response.data);
      setCorrelations(corrResponse.data ?? []);
    } finally {
      setLoading(false);
    }
  }, [tenantId, limit, correlationId, action]);

  useEffect(() => {
    void fetchLogs();
  }, [fetchLogs]);

  return (
    <>
      <Helmet>
        <title>Audit Trail | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Audit Trail</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Immutable log of platform actions. Filter by correlationId to inspect routing decisions end-to-end.
          </Typography>
        </Box>

        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
          <TextField
            label="Correlation ID"
            value={correlationId}
            onChange={(e) => setCorrelationId(e.target.value)}
            fullWidth
          />
          <TextField
            label="Action"
            value={action}
            onChange={(e) => setAction(e.target.value)}
            placeholder="RoutingDecision, HandoffCompleted..."
            fullWidth
          />
          <TextField
            label="Limit"
            type="number"
            value={limit}
            onChange={(e) => setLimit(Number(e.target.value || 100))}
            sx={{ minWidth: 120 }}
          />
          <Button variant="contained" onClick={() => void fetchLogs()}>Apply</Button>
        </Stack>

        <Stack direction="row" spacing={2} sx={{ mb: 3 }}>
          <Chip label={`${logs.length} events`} color="primary" variant="soft" />
          <Chip label={`${logs.filter((e) => e.severity === 'critical' || e.severity === 'error').length} issues`} color="error" variant="soft" />
          <Chip label={`${logs.filter((e) => e.severity === 'warning').length} warnings`} color="warning" variant="soft" />
        </Stack>

        <Card sx={{ mb: 3, p: 2, border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}` }}>
          <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Routing Timeline (recent correlation IDs)</Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {correlations.length === 0 ? (
              <Chip size="small" label="No correlation traces yet" />
            ) : (
              correlations.map((c) => (
                <Stack key={c.correlationId} direction="row" spacing={0.5} alignItems="center">
                  <Chip
                    size="small"
                    color={correlationId === c.correlationId ? 'primary' : 'default'}
                    label={`${c.correlationId} · ${c.eventCount}`}
                    onClick={() => setCorrelationId(c.correlationId)}
                    variant={correlationId === c.correlationId ? 'filled' : 'outlined'}
                  />
                  <Button size="small" variant="text" onClick={() => setSelectedCorrelation(c.correlationId)}>View</Button>
                </Stack>
              ))
            )}
          </Stack>
        </Card>

        <Card sx={{ overflow: 'hidden', border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}` }}>
          {loading ? (
            <LinearProgress />
          ) : (
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ fontWeight: 700 }}>Timestamp</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Actor</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Action</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Resource</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Correlation</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Severity</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>Details</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {logs.map((entry) => (
                    <TableRow key={entry.id} hover>
                      <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.8rem' }}>{new Date(entry.occurredAt).toLocaleString()}</TableCell>
                      <TableCell>{entry.actor || 'System'}</TableCell>
                      <TableCell><Chip label={entry.action} size="small" variant="outlined" /></TableCell>
                      <TableCell sx={{ maxWidth: 250, overflow: 'hidden', textOverflow: 'ellipsis' }}>{entry.resource || '-'}</TableCell>
                      <TableCell sx={{ maxWidth: 260, overflow: 'hidden', textOverflow: 'ellipsis', fontFamily: 'monospace', fontSize: '0.75rem' }}>{entry.correlationId || '-'}</TableCell>
                      <TableCell><Label color={severityColor(entry.severity)}>{entry.severity}</Label></TableCell>
                      <TableCell sx={{ maxWidth: 340, overflow: 'hidden', textOverflow: 'ellipsis', fontFamily: 'monospace', fontSize: '0.75rem' }}>{entry.eventJson || '-'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </Card>

        <Dialog open={!!selectedCorrelation} onClose={() => setSelectedCorrelation(null)} fullWidth maxWidth="md">
          <DialogTitle>
            <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
              <Typography variant="subtitle1">Routing Timeline · {selectedCorrelation}</Typography>
              <Stack direction="row" spacing={1}>
                <Button
                  size="small"
                  variant="outlined"
                  onClick={() => void copyToClipboard(JSON.stringify(selectedTimeline, null, 2), 'timeline JSON copied')}
                  disabled={selectedTimeline.length === 0}
                >
                  Export JSON
                </Button>
              </Stack>
            </Stack>
          </DialogTitle>
          <DialogContent dividers>
            <Stack spacing={1.5}>
              {selectedTimeline.length === 0 ? (
                <Typography variant="body2" color="text.secondary">No events loaded for this correlation in current filter range.</Typography>
              ) : (
                selectedTimeline.map((e, idx) => {
                  const prev = idx > 0 ? selectedTimeline[idx - 1] : null;
                  const changes = prev ? computeDiff(prev.eventJson, e.eventJson) : [];
                  const diffText = changes.map((c) => `${c.changeType} ${c.key}: ${JSON.stringify(c.from)} -> ${JSON.stringify(c.to)}`).join('\n');

                  return (
                    <Card key={e.id} variant="outlined" sx={{ p: 1.5 }}>
                      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
                        <Chip size="small" label={e.action} variant="outlined" />
                        <Label color={severityColor(e.severity)}>{e.severity}</Label>
                        <Typography variant="caption" color="text.secondary">{new Date(e.occurredAt).toLocaleString()}</Typography>
                        <Typography variant="caption" sx={{ fontFamily: 'monospace' }}>{e.resource || '-'}</Typography>
                        <Button size="small" variant="text" onClick={() => void copyToClipboard(formatJson(e.eventJson), 'event JSON copied')}>Copy JSON</Button>
                      </Stack>

                      {prev && (
                        <Box sx={{ mb: 1 }}>
                          <Stack direction="row" justifyContent="space-between" alignItems="center">
                            <Typography variant="caption" color="text.secondary">Diff vs previous event:</Typography>
                            <Button
                              size="small"
                              variant="text"
                              onClick={() => void copyToClipboard(diffText || 'No JSON field changes', 'event diff copied')}
                            >
                              Copy Diff
                            </Button>
                          </Stack>
                          {changes.length === 0 ? (
                            <Typography variant="caption" sx={{ display: 'block' }}>No JSON field changes</Typography>
                          ) : (
                            <Stack spacing={0.5} sx={{ mt: 0.5 }}>
                              {changes.slice(0, 8).map((c) => (
                                <Stack key={c.key} direction="row" spacing={0.75} alignItems="center">
                                  <Chip
                                    size="small"
                                    label={c.changeType}
                                    color={c.changeType === 'added' ? 'success' : c.changeType === 'removed' ? 'error' : 'warning'}
                                    variant="outlined"
                                  />
                                  <Typography variant="caption" sx={{ fontFamily: 'monospace' }}>
                                    {c.key}: {JSON.stringify(c.from)} → {JSON.stringify(c.to)}
                                  </Typography>
                                </Stack>
                              ))}
                              {changes.length > 8 && (
                                <Typography variant="caption" color="text.secondary">+{changes.length - 8} more changes...</Typography>
                              )}
                            </Stack>
                          )}
                        </Box>
                      )}

                      <Box component="pre" sx={{ m: 0, fontSize: '0.72rem', overflowX: 'auto', whiteSpace: 'pre-wrap' }}>
                        {formatJson(e.eventJson)}
                      </Box>
                    </Card>
                  );
                })
              )}
            </Stack>
          </DialogContent>
        </Dialog>

        <Snackbar open={!!copied} autoHideDuration={1800} onClose={() => setCopied(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}>
          <Alert severity="success" onClose={() => setCopied(null)} sx={{ width: '100%' }}>{copied}</Alert>
        </Snackbar>
      </DashboardContent>
    </>
  );
}
