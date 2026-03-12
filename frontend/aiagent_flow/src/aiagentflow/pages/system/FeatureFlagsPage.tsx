import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

const COMMON_FLAGS = [
  'evaluation.shadow.enabled',
  'evaluation.human_review.enabled',
  'segment.routing.enabled',
  'manager.handoff.enabled',
];

export default function FeatureFlagsPage() {
  const tenantId = useTenantId();
  const [agentId, setAgentId] = useState('');
  const [userId, setUserId] = useState('demo-user');
  const [segments, setSegments] = useState('beta,enterprise');
  const [enabledFlags, setEnabledFlags] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const contextPayload = {
    agentId: agentId || undefined,
    userId,
    userSegments: segments
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean),
    metadata: {},
  };

  const checkEnabled = async () => {
    try {
      setLoading(true);
      setMessage(null);
      const res = await axios.post(endpoints.agentflow.featureFlags.enabled(tenantId), contextPayload);
      setEnabledFlags(res.data?.enabledFeatures ?? []);
    } catch (e: any) {
      setMessage(e?.message ?? 'Failed to load enabled flags');
      setEnabledFlags([]);
    } finally {
      setLoading(false);
    }
  };

  const setFlag = async (flagKey: string, isEnabled: boolean) => {
    try {
      setLoading(true);
      await axios.put(endpoints.agentflow.featureFlags.update(tenantId, flagKey), {
        description: `Managed from UI for ${flagKey}`,
        isEnabled,
        targeting: {
          agentIds: agentId ? [agentId] : [],
          userSegments: contextPayload.userSegments,
          rolloutPercentage: 1.0,
        },
      });
      await checkEnabled();
      setMessage(`Flag ${flagKey} updated`);
    } catch (e: any) {
      setMessage(e?.message ?? `Failed to update ${flagKey}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <Helmet>
        <title>Feature Flags | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Typography variant="h4">Feature Flags</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Evaluate and manage experimentation flags by tenant context.
          </Typography>
        </Box>

        {message && <Alert severity="info" sx={{ mb: 2 }}>{message}</Alert>}

        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h6" sx={{ mb: 2 }}>Context</Typography>
            <Grid container spacing={2}>
              <Grid item xs={12} md={4}>
                <TextField fullWidth label="Agent ID (optional)" value={agentId} onChange={(e) => setAgentId(e.target.value)} />
              </Grid>
              <Grid item xs={12} md={4}>
                <TextField fullWidth label="User ID" value={userId} onChange={(e) => setUserId(e.target.value)} />
              </Grid>
              <Grid item xs={12} md={4}>
                <TextField fullWidth label="Segments (comma separated)" value={segments} onChange={(e) => setSegments(e.target.value)} />
              </Grid>
            </Grid>
            <Button sx={{ mt: 2 }} variant="contained" onClick={checkEnabled} disabled={loading}>Check Enabled Flags</Button>
          </CardContent>
        </Card>

        <Card>
          <CardContent>
            <Typography variant="h6" sx={{ mb: 2 }}>Common Flags</Typography>
            <Stack spacing={2}>
              {COMMON_FLAGS.map((flag) => {
                const isEnabled = enabledFlags.includes(flag);
                return (
                  <Box key={flag} sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{flag}</Typography>
                      <Chip size="small" label={isEnabled ? 'Enabled' : 'Disabled'} color={isEnabled ? 'success' : 'default'} />
                    </Stack>
                    <Switch checked={isEnabled} onChange={(_, checked) => setFlag(flag, checked)} disabled={loading} />
                  </Box>
                );
              })}
            </Stack>
          </CardContent>
        </Card>
      </DashboardContent>
    </>
  );
}
