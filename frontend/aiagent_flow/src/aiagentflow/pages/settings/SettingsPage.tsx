import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import CardHeader from '@mui/material/CardHeader';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

export default function SettingsPage() {
  return (
    <>
      <Helmet>
        <title>Settings | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Platform Settings</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Manage your tenant configuration, security settings, and platform preferences.
          </Typography>
        </Box>

        <Grid container spacing={3}>
          {/* Tenant Settings */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader
                title="Tenant Configuration"
                subheader="General settings for the current tenant"
                avatar={<Iconify icon="mdi:domain" width={28} />}
              />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <TextField
                    fullWidth
                    label="Tenant Name"
                    defaultValue="Acme Corporation"
                    variant="outlined"
                  />
                  <TextField
                    fullWidth
                    label="Tenant ID"
                    defaultValue="tenant-acme-001"
                    disabled
                    variant="outlined"
                  />
                  <TextField
                    fullWidth
                    label="Default API Version"
                    defaultValue="v1"
                    variant="outlined"
                  />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          {/* Security Settings */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader
                title="Security & Compliance"
                subheader="Control execution policies and access"
                avatar={<Iconify icon="mdi:shield-lock-outline" width={28} />}
              />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">Enforce RBAC</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Require role-based access for all endpoints
                      </Typography>
                    </Box>
                    <Switch defaultChecked />
                  </Box>
                  <Divider />
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">Prompt Injection Guard</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Scan all inputs for prompt injection attacks
                      </Typography>
                    </Box>
                    <Switch defaultChecked />
                  </Box>
                  <Divider />
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">Sandbox Dangerous Tools</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Run high-risk tools in isolated containers
                      </Typography>
                    </Box>
                    <Switch defaultChecked />
                  </Box>
                  <Divider />
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">Audit Logging</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Immutable logging of all platform events
                      </Typography>
                    </Box>
                    <Switch defaultChecked disabled />
                  </Box>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          {/* Execution Limits */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader
                title="Execution Limits"
                subheader="Control agent runtime boundaries"
                avatar={<Iconify icon="mdi:speedometer" width={28} />}
              />
              <Divider />
              <CardContent>
                <Stack spacing={3}>
                  <TextField
                    fullWidth
                    label="Max Steps per Execution"
                    type="number"
                    defaultValue={25}
                    variant="outlined"
                  />
                  <TextField
                    fullWidth
                    label="Timeout per Step (seconds)"
                    type="number"
                    defaultValue={30}
                    variant="outlined"
                  />
                  <TextField
                    fullWidth
                    label="Max Tokens per Execution"
                    type="number"
                    defaultValue={100000}
                    variant="outlined"
                  />
                  <TextField
                    fullWidth
                    label="Max Concurrent Executions"
                    type="number"
                    defaultValue={10}
                    variant="outlined"
                  />
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          {/* Observability */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardHeader
                title="Observability"
                subheader="OpenTelemetry and tracing configuration"
                avatar={<Iconify icon="mdi:chart-timeline-variant" width={28} />}
              />
              <Divider />
              <CardContent>
                <Stack spacing={2.5}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">OpenTelemetry Export</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Send traces to OTLP endpoint
                      </Typography>
                    </Box>
                    <Switch defaultChecked />
                  </Box>
                  <Divider />
                  <TextField
                    fullWidth
                    label="OTLP Endpoint"
                    defaultValue="http://localhost:4317"
                    variant="outlined"
                    size="small"
                  />
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">Execution Replay</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Record full execution traces for replay
                      </Typography>
                    </Box>
                    <Switch defaultChecked />
                  </Box>
                  <Divider />
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Box>
                      <Typography variant="subtitle2">LLM Decision Logging</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Log all LLM reasoning and planning steps
                      </Typography>
                    </Box>
                    <Switch defaultChecked />
                  </Box>
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        </Grid>

        <Box sx={{ mt: 4, display: 'flex', justifyContent: 'flex-end' }}>
          <Button
            variant="contained"
            size="large"
            startIcon={<Iconify icon="mdi:content-save-outline" />}
          >
            Save Settings
          </Button>
        </Box>
      </DashboardContent>
    </>
  );
}
