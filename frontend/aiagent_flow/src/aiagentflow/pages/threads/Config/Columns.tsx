import type { GridColDef } from '@mui/x-data-grid';

import { Chip, Stack, IconButton } from '@mui/material';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

export const getStatusChip = (status: string) => {
  const colorMap: Record<string, 'success' | 'warning' | 'error' | 'default'> = {
    Active: 'success',
    Archived: 'warning',
    Expired: 'error',
    Completed: 'default',
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
    width: 120,
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
