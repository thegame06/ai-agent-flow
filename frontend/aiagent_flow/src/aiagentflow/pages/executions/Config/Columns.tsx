import type { GridColDef } from '@mui/x-data-grid';

import { Label } from 'src/components/label';

export const EXECUTION_COLUMNS: GridColDef[] = [
  { field: 'id', headerName: 'Execution ID', width: 220 },
  { field: 'agentVersion', headerName: 'Agent Ver.', width: 120 },
  {
    field: 'status',
    headerName: 'Status',
    width: 150,
    renderCell: (params) => (
      <Label color={
        (params.value === 'Completed' && 'success') ||
        (params.value === 'Failed' && 'error') ||
        (params.value === 'Running' && 'warning') ||
        'info'
      }>
        {params.value}
      </Label>
    ),
  },
  { field: 'durationMs', headerName: 'Duration (ms)', width: 130 },
  { field: 'totalTokensUsed', headerName: 'Tokens', width: 100 },
  {
    field: 'createdAt',
    headerName: 'Started At',
    width: 200,
    valueGetter: (value) => new Date(value).toLocaleString(),
  },
];
