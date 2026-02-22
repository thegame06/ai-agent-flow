import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import { alpha, useTheme } from '@mui/material/styles';
import TableContainer from '@mui/material/TableContainer';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Label } from 'src/components/label';

// ----------------------------------------------------------------------

const severityColor = (severity: string) => {
  switch (severity) {
    case 'critical': return 'error';
    case 'error': return 'error';
    case 'warning': return 'warning';
    case 'success': return 'success';
    default: return 'info';
  }
};

// ----------------------------------------------------------------------

export default function AuditPage() {
  const theme = useTheme();
  const [logs, setLogs] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetch = async () => {
      try {
        const response = await axios.get('/api/v1/tenants/tenant-1/audit');
        setLogs(response.data);
      } finally {
        setLoading(false);
      }
    };
    fetch();
  }, []);

  return (
    <>
      <Helmet>
        <title>Audit Trail | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Audit Trail</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Immutable log of all platform actions for security compliance and forensic review.
          </Typography>
        </Box>

        {/* Summary chips */}
        <Stack direction="row" spacing={2} sx={{ mb: 3 }}>
          <Chip label={`${logs.length} events`} color="primary" variant="soft" />
          <Chip
            label={`${logs.filter(e => e.severity === 'critical' || e.severity === 'error').length} issues`}
            color="error"
            variant="soft"
          />
          <Chip
            label={`${logs.filter(e => e.severity === 'warning').length} warnings`}
            color="warning"
            variant="soft"
          />
        </Stack>

        <Card
          sx={{
            overflow: 'hidden',
            border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
          }}
        >
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
                    <TableCell sx={{ fontWeight: 700 }}>Severity</TableCell>
                    <TableCell sx={{ fontWeight: 700 }}>IP</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {logs.map((entry) => (
                    <TableRow key={entry.id} hover>
                      <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.8rem' }}>
                        {new Date(entry.occurredAt).toLocaleString()}
                      </TableCell>
                      <TableCell>{entry.actor || 'System'}</TableCell>
                      <TableCell>
                        <Chip label={entry.action} size="small" variant="outlined" />
                      </TableCell>
                      <TableCell sx={{ maxWidth: 250, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {entry.resource || '-'}
                      </TableCell>
                      <TableCell>
                        <Label color={severityColor(entry.severity)}>{entry.severity}</Label>
                      </TableCell>
                      <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.8rem' }}>
                        {entry.ip}
                      </TableCell>
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
