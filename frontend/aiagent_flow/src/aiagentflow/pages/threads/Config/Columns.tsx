import type { GridColDef } from '@mui/x-data-grid';

import { Chip, Stack, IconButton } from '@mui/material';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

export const getStatusChip = (status: string) => {
  const colorMap: Record<string, 'success' | 'warning' | 'error' | 'default' | 'info'> = {
    Active: 'success',
    Paused: 'info',
    Archived: 'warning',
    Expired: 'error',
    MaxTurnsReached: 'default',
  };

  return (
    <Chip label={status} size="small" color={colorMap[status] || 'default'} />
  );
};

// ----------------------------------------------------------------------

export const threadsColumns: GridColDef[] = [
  {
    field: 'threadKey',
    headerName: 'Thread Key',
    flex: 1,
    minWidth: 150,
    renderCell: (params) => (
      <span style={{ fontFamily: 'monospace', fontSize: '0.875rem' }}>
        {params.value}
      </span>
    ),
  },
  {
    field: 'agentName',
    headerName: 'Agent',
    flex: 1,
    minWidth: 150,
  },
  {
    field: 'status',
    headerName: 'Status',
    width: 120,
    renderCell: (params) => getStatusChip(params.value),
  },
  {
    field: 'assignedTo',
    headerName: 'Assigned',
    width: 140,
    valueFormatter: (value) => value || 'Unassigned',
  },
  {
    field: 'channel',
    headerName: 'Channel',
    width: 120,
    valueFormatter: (value) => value || 'N/A',
  },
  {
    field: 'tags',
    headerName: 'Tags',
    width: 180,
    renderCell: (params) => (Array.isArray(params.value) && params.value.length > 0 ? params.value.join(', ') : 'N/A'),
  },
  {
    field: 'slaDueAt',
    headerName: 'SLA',
    width: 170,
    renderCell: (params) => {
      const value = params.value as string | undefined;
      if (!value) return 'N/A';
      const dueAt = new Date(value).getTime();
      const isOverdue = Number.isFinite(dueAt) && dueAt < Date.now();
      return (
        <Chip
          size="small"
          label={new Date(value).toLocaleString()}
          color={isOverdue ? 'error' : 'default'}
          variant={isOverdue ? 'filled' : 'outlined'}
        />
      );
    },
  },
  {
    field: 'turnCount',
    headerName: 'Turns',
    width: 80,
    type: 'number',
    renderCell: (params) => `${params.value}/${params.row.maxTurns}`,
  },
  {
    field: 'createdAt',
    headerName: 'Created',
    width: 150,
    type: 'dateTime',
    valueFormatter: (value) => value ? new Date(value).toLocaleString() : 'N/A',
  },
  {
    field: 'lastActivityAt',
    headerName: 'Last Activity',
    width: 150,
    type: 'dateTime',
    valueFormatter: (value) => value ? new Date(value).toLocaleString() : 'N/A',
  },
  {
    field: 'actions',
    headerName: 'Actions',
    width: 160,
    sortable: false,
    renderCell: (params) => (
      <Stack direction="row" spacing={1}>
        <IconButton
          size="small"
          color="primary"
          onClick={() => params.row.onOpenChat?.(params.row.id)}
          title="Open Chat"
        >
          <Iconify icon="solar:chat-round-line-duotone" />
        </IconButton>
        <IconButton
          size="small"
          color="info"
          onClick={() => params.row.onEditInbox?.(params.row.id)}
          title="Edit Inbox"
        >
          <Iconify icon="solar:settings-linear" />
        </IconButton>
        {params.row.status === 'Active' && (
          <IconButton
            size="small"
            color="warning"
            onClick={() => params.row.onArchive?.(params.row.id)}
            title="Archive"
          >
            <Iconify icon="solar:archive-minimalistic-line-duotone" />
          </IconButton>
        )}
        <IconButton
          size="small"
          color="error"
          onClick={() => params.row.onDelete?.(params.row.id)}
          title="Delete"
        >
          <Iconify icon="mingcute:delete-line" />
        </IconButton>
      </Stack>
    ),
  },
];
