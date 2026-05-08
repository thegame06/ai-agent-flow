import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import { DataGrid, GridToolbar } from '@mui/x-data-grid';

import { paths } from 'src/routes/paths';
import { useRouter } from 'src/routes/hooks';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { useThreads } from './Hooks/useThreads';
import { threadsColumns } from './Config/Columns';

// ----------------------------------------------------------------------

export default function ThreadsPage() {
  const router = useRouter();
  const tenantId = useTenantId();
  const { threads, metrics, loading, error, loadThreads, loadThreadMetrics, archiveThreadById, updateThreadInboxById, deleteThreadById } = useThreads(tenantId);

  const [filterAgent, setFilterAgent] = useState<string>('');
  const [filterStatus, setFilterStatus] = useState<string>('Active');
  const [selectedThread, setSelectedThread] = useState<string | null>(null);
  const [openArchiveDialog, setOpenArchiveDialog] = useState(false);
  const [openDeleteDialog, setOpenDeleteDialog] = useState(false);
  const [openInboxDialog, setOpenInboxDialog] = useState(false);
  const [inboxForm, setInboxForm] = useState({
    assignedTo: '',
    status: 'Active',
    tags: '',
    channel: '',
    slaDueAt: '',
    internalNote: '',
  });

  const handleOpenChat = (threadId: string) => {
    const thread = threads.find((t) => t.id === threadId);
    if (thread) {
      router.push(`${paths.dashboard.agents}/${thread.agentId}/chat?thread=${threadId}`);
    }
  };

  const handleArchive = (threadId: string) => {
    setSelectedThread(threadId);
    setOpenArchiveDialog(true);
  };

  const handleDelete = (threadId: string) => {
    setSelectedThread(threadId);
    setOpenDeleteDialog(true);
  };

  const handleEditInbox = (threadId: string) => {
    const thread = threads.find((t) => t.id === threadId);
    if (!thread) return;
    setSelectedThread(threadId);
    setInboxForm({
      assignedTo: thread.assignedTo || '',
      status: thread.status || 'Active',
      tags: (thread.tags || []).join(','),
      channel: thread.channel || '',
      slaDueAt: thread.slaDueAt ? new Date(thread.slaDueAt).toISOString().slice(0, 16) : '',
      internalNote: thread.internalNote || '',
    });
    setOpenInboxDialog(true);
  };

  const confirmArchive = async () => {
    if (selectedThread) {
      await archiveThreadById(selectedThread);
      setOpenArchiveDialog(false);
      setSelectedThread(null);
      await loadThreads(filterAgent || undefined, filterStatus || undefined, 100);
    }
  };

  const confirmDelete = async () => {
    if (selectedThread) {
      await deleteThreadById(selectedThread);
      setOpenDeleteDialog(false);
      setSelectedThread(null);
      await loadThreads(filterAgent || undefined, filterStatus || undefined, 100);
    }
  };

  const handleRefresh = async () => {
    await loadThreads(filterAgent || undefined, filterStatus || undefined, 100);
    await loadThreadMetrics(filterAgent || undefined);
  };

  const handleSaveInbox = async () => {
    if (!selectedThread) return;
    await updateThreadInboxById({
      threadId: selectedThread,
      assignedTo: inboxForm.assignedTo || undefined,
      status: inboxForm.status || undefined,
      tags: inboxForm.tags ? inboxForm.tags.split(',').map((t) => t.trim()).filter(Boolean) : [],
      channel: inboxForm.channel || undefined,
      slaDueAt: inboxForm.slaDueAt ? new Date(inboxForm.slaDueAt).toISOString() : undefined,
      internalNote: inboxForm.internalNote || undefined,
    });
    setOpenInboxDialog(false);
    setSelectedThread(null);
    await handleRefresh();
  };

  const filteredThreads = threads
    .filter((thread) => !filterStatus || thread.status === filterStatus)
    .filter((thread) => !filterAgent || thread.agentId === filterAgent);

  const threadsWithActions = filteredThreads.map((thread) => ({
    ...thread,
    onOpenChat: handleOpenChat,
    onEditInbox: handleEditInbox,
    onArchive: handleArchive,
    onDelete: handleDelete,
  }));

  const avgFirstResponse = metrics?.avgFirstResponseMinutes ?? 0;
  const resolutionRate = metrics?.resolutionRatePercent ?? 0;
  const threadsPerAgent = metrics?.threadsPerAgent ?? 0;
  const slaBreaches = metrics?.slaBreaches ?? 0;

  const handleAutoAssign = async () => {
    const unassigned = filteredThreads.filter((t) => !t.assignedTo);
    for (const thread of unassigned) {
      await updateThreadInboxById({
        threadId: thread.id,
        assignedTo: thread.userId || 'owner',
      });
    }
    await handleRefresh();
  };

  return (
    <>
      <Helmet>
        <title>Conversation Threads | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Conversation Threads</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
              Manage and view conversation history across all agents.
            </Typography>
          </Box>
          <Button variant="outlined" startIcon={<Iconify icon="solar:refresh-line-duotone" />} onClick={handleRefresh}>
            Refresh
          </Button>
        </Box>

        {error && (
          <Box sx={{ mb: 2 }}>
            <Typography color="error">{error}</Typography>
          </Box>
        )}

        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Avg First Response</Typography>
            <Typography variant="h5">{avgFirstResponse} min</Typography>
          </Card>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Resolution Rate</Typography>
            <Typography variant="h5">{resolutionRate}%</Typography>
          </Card>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Threads per Agent</Typography>
            <Typography variant="h5">{threadsPerAgent}</Typography>
          </Card>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">SLA Breaches</Typography>
            <Typography variant="h5" color={slaBreaches > 0 ? 'error.main' : 'text.primary'}>{slaBreaches}</Typography>
          </Card>
        </Stack>
        <Card sx={{ p: 2, mb: 2 }}>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>Backlog by Channel</Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap">
            {Object.entries(metrics?.backlogByChannel || {}).map(([key, value]) => (
              <Chip key={key} label={`${key}: ${value}`} size="small" />
            ))}
          </Stack>
          <Typography variant="subtitle2" sx={{ mt: 2, mb: 1 }}>Backlog by Status</Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap">
            {Object.entries(metrics?.backlogByStatus || {}).map(([key, value]) => (
              <Chip key={key} label={`${key}: ${value}`} size="small" />
            ))}
          </Stack>
        </Card>

        <Card sx={{ p: 2 }}>
          {/* Filters */}
          <Stack direction="row" spacing={2} sx={{ mb: 3 }}>
            <TextField
              select
              label="Filter by Agent"
              value={filterAgent}
              onChange={(e) => setFilterAgent(e.target.value)}
              size="small"
              sx={{ width: 200 }}
            >
              <MenuItem value="">All Agents</MenuItem>
              {/* Could populate from agents list if needed */}
            </TextField>

            <TextField
              select
              label="Filter by Status"
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value)}
              size="small"
              sx={{ width: 150 }}
            >
              <MenuItem value="">All Statuses</MenuItem>
              <MenuItem value="Active">Active</MenuItem>
              <MenuItem value="Archived">Archived</MenuItem>
              <MenuItem value="Expired">Expired</MenuItem>
              <MenuItem value="Paused">Paused</MenuItem>
              <MenuItem value="MaxTurnsReached">MaxTurnsReached</MenuItem>
            </TextField>
            <Button variant="contained" color="info" onClick={handleAutoAssign}>
              Auto-assign Unassigned
            </Button>
          </Stack>

          {/* DataGrid */}
          <Box sx={{ height: 600 }}>
            <DataGrid
              rows={threadsWithActions}
              columns={threadsColumns}
              loading={loading}
              getRowId={(row) => row.id}
              slots={{
                toolbar: GridToolbar,
              }}
              slotProps={{
                toolbar: {
                  showQuickFilter: true,
                  quickFilterProps: { placeholder: 'Search threads...' },
                },
              }}
              initialState={{
                pagination: {
                  paginationModel: { pageSize: 20 },
                },
                sorting: {
                  sortModel: [{ field: 'lastActivityAt', sort: 'desc' }],
                },
              }}
              pageSizeOptions={[10, 20, 50, 100]}
              disableRowSelectionOnClick
            />
          </Box>
        </Card>
      </DashboardContent>

      {/* Archive Confirmation Dialog */}
      <Dialog open={openArchiveDialog} onClose={() => setOpenArchiveDialog(false)}>
        <DialogTitle>Archive Thread</DialogTitle>
        <DialogContent>
          <Typography>Are you sure you want to archive this thread? It will be moved to archived status.</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenArchiveDialog(false)}>Cancel</Button>
          <Button variant="contained" color="warning" onClick={confirmArchive}>
            Archive
          </Button>
        </DialogActions>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={openDeleteDialog} onClose={() => setOpenDeleteDialog(false)}>
        <DialogTitle>Delete Thread</DialogTitle>
        <DialogContent>
          <Typography color="error">
            Are you sure you want to permanently delete this thread? This action cannot be undone.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenDeleteDialog(false)}>Cancel</Button>
          <Button variant="contained" color="error" onClick={confirmDelete}>
            Delete
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={openInboxDialog} onClose={() => setOpenInboxDialog(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Edit Inbox Metadata</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Assigned To" value={inboxForm.assignedTo} onChange={(e) => setInboxForm((prev) => ({ ...prev, assignedTo: e.target.value }))} fullWidth />
            <TextField select label="Status" value={inboxForm.status} onChange={(e) => setInboxForm((prev) => ({ ...prev, status: e.target.value }))} fullWidth>
              <MenuItem value="Active">Active</MenuItem>
              <MenuItem value="Paused">Paused</MenuItem>
              <MenuItem value="Archived">Archived</MenuItem>
            </TextField>
            <TextField label="Channel" value={inboxForm.channel} onChange={(e) => setInboxForm((prev) => ({ ...prev, channel: e.target.value }))} fullWidth />
            <TextField label="Tags (comma separated)" value={inboxForm.tags} onChange={(e) => setInboxForm((prev) => ({ ...prev, tags: e.target.value }))} fullWidth />
            <TextField type="datetime-local" label="SLA Due At" value={inboxForm.slaDueAt} onChange={(e) => setInboxForm((prev) => ({ ...prev, slaDueAt: e.target.value }))} fullWidth InputLabelProps={{ shrink: true }} />
            <TextField label="Internal Note" value={inboxForm.internalNote} onChange={(e) => setInboxForm((prev) => ({ ...prev, internalNote: e.target.value }))} multiline minRows={3} fullWidth />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenInboxDialog(false)}>Cancel</Button>
          <Button variant="contained" onClick={handleSaveInbox}>Save</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
