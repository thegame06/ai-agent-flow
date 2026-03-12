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
  const { threads, loading, error, loadThreads, archiveThreadById, deleteThreadById } = useThreads(tenantId);

  const [filterAgent, setFilterAgent] = useState<string>('');
  const [filterStatus, setFilterStatus] = useState<string>('Active');
  const [selectedThread, setSelectedThread] = useState<string | null>(null);
  const [openArchiveDialog, setOpenArchiveDialog] = useState(false);
  const [openDeleteDialog, setOpenDeleteDialog] = useState(false);

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
  };

  const threadsWithActions = threads.map((thread) => ({
    ...thread,
    onOpenChat: handleOpenChat,
    onArchive: handleArchive,
    onDelete: handleDelete,
  }));

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
              <MenuItem value="Completed">Completed</MenuItem>
            </TextField>
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
    </>
  );
}
