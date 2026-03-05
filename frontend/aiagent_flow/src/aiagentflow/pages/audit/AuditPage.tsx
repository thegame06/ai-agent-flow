import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { alpha, useTheme } from '@mui/material/styles';
import TableContainer from '@mui/material/TableContainer';
import LinearProgress from '@mui/material/LinearProgress';

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

  const fetchLogs = useCallback(async () => {
    try {
      setLoading(true);
      const params = new URLSearchParams();
      params.set('limit', String(limit));
      if (correlationId.trim()) params.set('correlationId', correlationId.trim());
      if (action.trim()) params.set('action', action.trim());

      const response = await axios.get(`${endpoints.agentflow.audit.list(tenantId)}?${params.toString()}`);
      setLogs(response.data);
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
      </DashboardContent>
    </>
  );
}
