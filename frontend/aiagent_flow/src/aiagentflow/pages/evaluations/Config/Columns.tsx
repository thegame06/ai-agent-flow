import type { GridColDef } from '@mui/x-data-grid';

import { Chip, Stack, Typography, LinearProgress } from '@mui/material';

// ----------------------------------------------------------------------

export const getStatusChip = (status: string) => {
  const colorMap: Record<string, 'success' | 'warning' | 'error' | 'info'> = {
    Completed: 'success',
    Pending: 'warning',
    Failed: 'error',
  };

  return (
    <Chip label={status} size="small" color={colorMap[status] || 'info'} />
  );
};

export const getScoreDisplay = (score?: number) => {
  if (score === undefined || score === null) return 'N/A';
  
  const color = score >= 80 ? 'success.main' : score >= 60 ? 'warning.main' : 'error.main';
  
  return (
    <Stack direction="row" spacing={1} alignItems="center">
      <LinearProgress
        variant="determinate"
        value={score}
        sx={{
          width: 80,
          height: 8,
          borderRadius: 1,
          bgcolor: 'grey.300',
          '& .MuiLinearProgress-bar': {
            bgcolor: color,
          },
        }}
      />
      <Typography variant="body2" fontWeight={600} sx={{ minWidth: 40 }}>
        {score.toFixed(0)}%
      </Typography>
    </Stack>
  );
};

// ----------------------------------------------------------------------

export const evaluationsColumns: GridColDef[] = [
  {
    field: 'runId',
    headerName: 'Run ID',
    flex: 1,
    minWidth: 120,
    renderCell: (params) => (
      <span style={{ fontFamily: 'monospace', fontSize: '0.875rem' }}>
        {params.value?.slice(0, 8)}...
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
    field: 'executionId',
    headerName: 'Execution',
    flex: 1,
    minWidth: 120,
    renderCell: (params) => (
      <span style={{ fontFamily: 'monospace', fontSize: '0.875rem' }}>
        {params.value?.slice(0, 8)}...
      </span>
    ),
  },
  {
    field: 'status',
    headerName: 'Status',
    width: 120,
    renderCell: (params) => getStatusChip(params.value),
  },
  {
    field: 'overallScore',
    headerName: 'Score',
    width: 150,
    renderCell: (params) => getScoreDisplay(params.value),
  },
  {
    field: 'createdAt',
    headerName: 'Created',
    width: 150,
    type: 'dateTime',
    valueFormatter: (value) => value ? new Date(value).toLocaleString() : 'N/A',
  },
  {
    field: 'reviewerId',
    headerName: 'Reviewer',
    width: 120,
    renderCell: (params) => params.value || 'Pending',
  },
];

// ----------------------------------------------------------------------

export const pendingReviewColumns: GridColDef[] = [
  {
    field: 'agentName',
    headerName: 'Agent',
    flex: 1,
    minWidth: 150,
  },
  {
    field: 'executionId',
    headerName: 'Execution',
    flex: 1,
    minWidth: 120,
  },
  {
    field: 'overallScore',
    headerName: 'Score',
    width: 150,
    renderCell: (params) => getScoreDisplay(params.value),
  },
  {
    field: 'createdAt',
    headerName: 'Created',
    width: 150,
    type: 'dateTime',
    valueFormatter: (value) => value ? new Date(value).toLocaleString() : 'N/A',
  },
  {
    field: 'notes',
    headerName: 'Notes',
    flex: 1,
    minWidth: 200,
    renderCell: (params) => params.value || '—',
  },
];
