import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
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
        <title>Hilos de conversacion | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper
          variant="outlined"
          sx={{
            mb: 3,
            p: { xs: 2.5, md: 3 },
            borderRadius: 4,
            background:
              'radial-gradient(circle at 8% 18%, rgba(0,167,181,0.14), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ md: 'center' }}>
            <Stack direction="row" spacing={1.5} alignItems="center">
              <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.lighter', color: 'primary.main' }}>
                <Iconify icon="mdi:inbox-outline" width={30} />
              </Avatar>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Inbox omnicanal
                </Typography>
                <Typography variant="h3">Bandeja de entrada</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                  Opera conversaciones, backlog, asignaciones y SLA desde una sola vista.
                </Typography>
              </Box>
            </Stack>
            <Button variant="outlined" startIcon={<Iconify icon="solar:refresh-line-duotone" />} onClick={handleRefresh}>
              Actualizar
            </Button>
          </Stack>
        </Paper>

        {error && (
          <Box sx={{ mb: 2 }}>
            <Typography color="error">{error}</Typography>
          </Box>
        )}

        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Primera respuesta prom.</Typography>
            <Typography variant="h5">{avgFirstResponse} min</Typography>
          </Card>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Tasa de resolucion</Typography>
            <Typography variant="h5">{resolutionRate}%</Typography>
          </Card>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Hilos por agente</Typography>
            <Typography variant="h5">{threadsPerAgent}</Typography>
          </Card>
          <Card sx={{ p: 2, flex: 1 }}>
            <Typography variant="subtitle2" color="text.secondary">Incumplimientos SLA</Typography>
            <Typography variant="h5" color={slaBreaches > 0 ? 'error.main' : 'text.primary'}>{slaBreaches}</Typography>
          </Card>
        </Stack>
        <Card sx={{ p: 2, mb: 2 }}>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>Backlog por canal</Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap">
            {Object.entries(metrics?.backlogByChannel || {}).map(([key, value]) => (
              <Chip key={key} label={`${key}: ${value}`} size="small" />
            ))}
          </Stack>
          <Typography variant="subtitle2" sx={{ mt: 2, mb: 1 }}>Backlog por estado</Typography>
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
              label="Filtrar por agente"
              value={filterAgent}
              onChange={(e) => setFilterAgent(e.target.value)}
              size="small"
              sx={{ width: 200 }}
            >
              <MenuItem value="">Todos los agentes</MenuItem>
              {/* Could populate from agents list if needed */}
            </TextField>

            <TextField
              select
              label="Filtrar por estado"
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value)}
              size="small"
              sx={{ width: 150 }}
            >
              <MenuItem value="">Todos los estados</MenuItem>
              <MenuItem value="Active">Active</MenuItem>
              <MenuItem value="Archived">Archived</MenuItem>
              <MenuItem value="Expired">Expired</MenuItem>
              <MenuItem value="Paused">Paused</MenuItem>
              <MenuItem value="MaxTurnsReached">MaxTurnsReached</MenuItem>
            </TextField>
            <Button variant="contained" color="info" onClick={handleAutoAssign}>
              Autoasignar sin asignacion
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
                  quickFilterProps: { placeholder: 'Buscar hilos...' },
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
        <DialogTitle>Archivar hilo</DialogTitle>
        <DialogContent>
          <Typography>Seguro que deseas archivar este hilo? Se movera al estado archivado.</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenArchiveDialog(false)}>Cancelar</Button>
          <Button variant="contained" color="warning" onClick={confirmArchive}>
            Archivar
          </Button>
        </DialogActions>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={openDeleteDialog} onClose={() => setOpenDeleteDialog(false)}>
        <DialogTitle>Eliminar hilo</DialogTitle>
        <DialogContent>
          <Typography color="error">
            Seguro que deseas eliminar este hilo permanentemente? Esta accion no se puede deshacer.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenDeleteDialog(false)}>Cancelar</Button>
          <Button variant="contained" color="error" onClick={confirmDelete}>
            Eliminar
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={openInboxDialog} onClose={() => setOpenInboxDialog(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Editar metadata de inbox</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Asignado a" value={inboxForm.assignedTo} onChange={(e) => setInboxForm((prev) => ({ ...prev, assignedTo: e.target.value }))} fullWidth />
            <TextField select label="Estado" value={inboxForm.status} onChange={(e) => setInboxForm((prev) => ({ ...prev, status: e.target.value }))} fullWidth>
              <MenuItem value="Active">Active</MenuItem>
              <MenuItem value="Paused">Paused</MenuItem>
              <MenuItem value="Archived">Archived</MenuItem>
            </TextField>
            <TextField label="Canal" value={inboxForm.channel} onChange={(e) => setInboxForm((prev) => ({ ...prev, channel: e.target.value }))} fullWidth />
            <TextField label="Etiquetas (separadas por coma)" value={inboxForm.tags} onChange={(e) => setInboxForm((prev) => ({ ...prev, tags: e.target.value }))} fullWidth />
            <TextField type="datetime-local" label="SLA vence en" value={inboxForm.slaDueAt} onChange={(e) => setInboxForm((prev) => ({ ...prev, slaDueAt: e.target.value }))} fullWidth InputLabelProps={{ shrink: true }} />
            <TextField label="Nota interna" value={inboxForm.internalNote} onChange={(e) => setInboxForm((prev) => ({ ...prev, internalNote: e.target.value }))} multiline minRows={3} fullWidth />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenInboxDialog(false)}>Cancelar</Button>
          <Button variant="contained" onClick={handleSaveInbox}>Guardar</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
