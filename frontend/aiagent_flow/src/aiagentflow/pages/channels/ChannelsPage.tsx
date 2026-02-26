import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TextField from '@mui/material/TextField';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Iconify } from 'src/components/iconify';

const TENANT_ID = 'tenant-1';

interface Channel {
  id: string;
  name: string;
  type: string;
  status: string;
  config: Record<string, string>;
  createdAt: string;
  lastActivityAt?: string;
}

interface ChannelSession {
  id: string;
  channelId: string;
  channelType: string;
  identifier: string;
  agentId?: string;
  threadId?: string;
  status: string;
  messageCount: number;
  createdAt: string;
  lastActivityAt: string;
  expiresAt?: string;
}

export default function ChannelsPage() {
  const [channels, setChannels] = useState<Channel[]>([]);
  const [sessions, setSessions] = useState<ChannelSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [openCreate, setOpenCreate] = useState(false);
  const [qrCode, setQrCode] = useState<string | null>(null);
  const [selectedChannel, setSelectedChannel] = useState<Channel | null>(null);

  const [form, setForm] = useState({
    name: '',
    type: 'WhatsApp',
    authMode: 'qr',
    apiToken: '',
    phoneNumberId: '',
    defaultAgentId: '',
  });

  const channelTypes = [
    { value: 'WhatsApp', label: 'WhatsApp', icon: 'mdi:whatsapp' },
    { value: 'WebChat', label: 'Web Chat', icon: 'mdi:web' },
    { value: 'Api', label: 'API Direct', icon: 'mdi:api' },
    { value: 'Telegram', label: 'Telegram', icon: 'mdi:telegram' },
    { value: 'Slack', label: 'Slack', icon: 'mdi:slack' },
  ];

  const fetchAll = async () => {
    try {
      setLoading(true);
      setError(null);
      const [channelsRes, sessionsRes] = await Promise.all([
        axios.get(`/api/v1/tenants/${TENANT_ID}/channels`),
        axios.get(`/api/v1/tenants/${TENANT_ID}/channel-sessions?limit=50`),
      ]);

      setChannels((channelsRes.data ?? []) as Channel[]);
      setSessions((sessionsRes.data ?? []) as ChannelSession[]);
    } catch (err: any) {
      setError(err?.message || 'Failed to load channels');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchAll();
  }, []);

  const handleCreate = async () => {
    if (!form.name.trim()) return;

    try {
      setSaving(true);
      const config: Record<string, string> = {
        AuthMode: form.authMode,
        DefaultAgentId: form.defaultAgentId || 'default-agent',
      };

      if (form.authMode === 'business') {
        config.ApiToken = form.apiToken;
        config.PhoneNumberId = form.phoneNumberId;
      }

      await axios.post(`/api/v1/tenants/${TENANT_ID}/channels`, {
        name: form.name.trim(),
        type: form.type,
        config,
      });

      setOpenCreate(false);
      setForm({ name: '', type: 'WhatsApp', authMode: 'qr', apiToken: '', phoneNumberId: '', defaultAgentId: '' });
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Failed to create channel');
    } finally {
      setSaving(false);
    }
  };

  const fetchQrCode = async (channelId: string) => {
    const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}/qr`);
    return res.data?.qrCode as string | undefined;
  };

  const handleActivate = async (channel: Channel) => {
    try {
      const res = await axios.post(`/api/v1/tenants/${TENANT_ID}/channels/${channel.id}/activate`);

      if (channel.type === 'WhatsApp' && channel.config?.AuthMode === 'qr') {
        setSelectedChannel(channel);

        // Poll QR endpoint for a short window while bridge initializes session.
        let qr: string | undefined;
        for (let i = 0; i < 8 && !qr; i++) {
          try {
            qr = await fetchQrCode(channel.id);
          } catch {
            // ignore while QR still unavailable
          }
          if (!qr) await new Promise((r) => setTimeout(r, 1500));
        }

        if (qr) {
          setQrCode(qr);
        } else {
          alert('Channel activated, but QR not available yet. Use Refresh QR.');
        }
      }

      alert(`Channel activated: ${res.data.status}`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Failed to activate channel');
    }
  };

  const handleDeactivate = async (channelId: string) => {
    if (!confirm('Deactivate this channel?')) return;

    try {
      await axios.post(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}/deactivate`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Failed to deactivate channel');
    }
  };

  const handleDelete = async (channelId: string) => {
    if (!confirm('Delete this channel permanently?')) return;

    try {
      await axios.delete(`/api/v1/tenants/${TENANT_ID}/channels/${channelId}`);
      await fetchAll();
    } catch (err: any) {
      alert(err?.message || 'Failed to delete channel');
    }
  };

  const handleCheckHealth = async (channel: Channel) => {
    try {
      const res = await axios.get(`/api/v1/tenants/${TENANT_ID}/channels/${channel.id}/status`);
      const qrSuffix = channel.type === 'WhatsApp' && channel.config?.AuthMode === 'qr'
        ? ` | QR: ${res.data.qrAvailable ? 'AVAILABLE' : 'PENDING'}`
        : '';
      alert(`Health: ${res.data.healthy ? 'OK' : 'UNHEALTHY'} - ${res.data.message || 'n/a'}${qrSuffix}`);
    } catch (err: any) {
      alert(err?.message || 'Health check failed');
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Active': return 'success';
      case 'Error': return 'error';
      case 'Maintenance': return 'warning';
      default: return 'default';
    }
  };

  return (
    <>
      <Helmet>
        <title>Channels | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Communication Channels</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
              Manage WhatsApp, Web Chat, API and other communication channels for your agents.
            </Typography>
          </Box>
          <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={() => setOpenCreate(true)}>
            Add Channel
          </Button>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={7}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Channels</Typography>
              {loading ? (
                <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress /></Box>
              ) : channels.length === 0 ? (
                <Alert severity="info">No channels configured yet.</Alert>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Name</TableCell>
                      <TableCell>Type</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Activity</TableCell>
                      <TableCell align="right">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {channels.map((c) => (
                      <TableRow key={c.id} hover>
                        <TableCell>{c.name}</TableCell>
                        <TableCell>
                          <Chip label={c.type} size="small" variant="outlined" />
                        </TableCell>
                        <TableCell>
                          <Chip label={c.status} size="small" color={getStatusColor(c.status) as any} />
                        </TableCell>
                        <TableCell>
                          {c.lastActivityAt ? new Date(c.lastActivityAt).toLocaleString() : 'Never'}
                        </TableCell>
                        <TableCell align="right">
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            {c.status === 'Active' ? (
                              <Button size="small" variant="outlined" color="warning" onClick={() => handleDeactivate(c.id)}>
                                Deactivate
                              </Button>
                            ) : (
                              <Button size="small" variant="outlined" color="success" onClick={() => handleActivate(c)}>
                                Activate
                              </Button>
                            )}
                            <IconButton size="small" onClick={() => handleCheckHealth(c)}>
                              <Iconify icon="mdi:heart-pulse" />
                            </IconButton>
                            <IconButton size="small" color="error" onClick={() => handleDelete(c.id)}>
                              <Iconify icon="mingcute:delete-line" />
                            </IconButton>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Card>
          </Grid>

          <Grid item xs={12} md={5}>
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 2 }}>Active Sessions</Typography>
              {sessions.length === 0 ? (
                <Alert severity="info">No active sessions.</Alert>
              ) : (
                <Stack spacing={2} sx={{ maxHeight: 600, overflow: 'auto' }}>
                  {sessions.slice(0, 10).map((s) => (
                    <Box key={s.id} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}>
                      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                        <Chip label={s.channelType} size="small" />
                        <Typography variant="caption">{s.messageCount} msgs</Typography>
                      </Box>
                      <Typography variant="body2" fontWeight={700}>{s.identifier}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Agent: {s.agentId ?? '—'} • Thread: {s.threadId?.slice(0, 8) ?? '—'}
                      </Typography>
                      <Typography variant="caption" display="block" sx={{ mt: 0.5 }}>
                        Last: {new Date(s.lastActivityAt).toLocaleString()}
                      </Typography>
                    </Box>
                  ))}
                </Stack>
              )}
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      {/* Create Channel Dialog */}
      <Dialog open={openCreate} onClose={() => setOpenCreate(false)} fullWidth maxWidth="sm">
        <DialogTitle>Create Channel</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <TextField
              label="Channel Name"
              value={form.name}
              onChange={(e) => setForm((p) => ({ ...p, name: e.target.value }))}
              placeholder="WhatsApp Support"
              fullWidth
            />

            <TextField
              select
              label="Channel Type"
              value={form.type}
              onChange={(e) => setForm((p) => ({ ...p, type: e.target.value }))}
              fullWidth
            >
              {channelTypes.map((t) => (
                <MenuItem key={t.value} value={t.value}>
                  {t.label}
                </MenuItem>
              ))}
            </TextField>

            {form.type === 'WhatsApp' && (
              <>
                <TextField
                  select
                  label="Authentication Mode"
                  value={form.authMode}
                  onChange={(e) => setForm((p) => ({ ...p, authMode: e.target.value }))}
                  fullWidth
                >
                  <MenuItem value="qr">QR Code (like OpenClaw)</MenuItem>
                  <MenuItem value="business">WhatsApp Business API</MenuItem>
                </TextField>

                {form.authMode === 'business' && (
                  <>
                    <TextField
                      label="API Token"
                      value={form.apiToken}
                      onChange={(e) => setForm((p) => ({ ...p, apiToken: e.target.value }))}
                      placeholder="EAAB..."
                      fullWidth
                      type="password"
                    />
                    <TextField
                      label="Phone Number ID"
                      value={form.phoneNumberId}
                      onChange={(e) => setForm((p) => ({ ...p, phoneNumberId: e.target.value }))}
                      placeholder="123456789"
                      fullWidth
                    />
                  </>
                )}
              </>
            )}

            <TextField
              label="Default Agent ID"
              value={form.defaultAgentId}
              onChange={(e) => setForm((p) => ({ ...p, defaultAgentId: e.target.value }))}
              placeholder="gps-support-agent"
              fullWidth
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenCreate(false)}>Cancel</Button>
          <Button variant="contained" onClick={handleCreate} disabled={saving || !form.name}>
            {saving ? 'Creating...' : 'Create'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* QR Code Dialog */}
      <Dialog open={!!qrCode} onClose={() => setQrCode(null)} maxWidth="sm">
        <DialogTitle>Scan QR Code - {selectedChannel?.name}</DialogTitle>
        <DialogContent>
          <Box sx={{ textAlign: 'center', py: 3 }}>
            {qrCode && (
              <>
                <img src={qrCode} alt="WhatsApp QR" style={{ maxWidth: '100%', height: 'auto' }} />
                <Alert severity="info" sx={{ mt: 2 }}>
                  Open WhatsApp on your phone → Settings → Linked Devices → Link a Device → Scan this QR code
                </Alert>
              </>
            )}
          </Box>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={async () => {
              if (!selectedChannel) return;
              try {
                const qr = await fetchQrCode(selectedChannel.id);
                if (qr) setQrCode(qr);
                else alert('QR still not available');
              } catch {
                alert('QR still not available');
              }
            }}
          >
            Refresh QR
          </Button>
          <Button onClick={() => setQrCode(null)}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
