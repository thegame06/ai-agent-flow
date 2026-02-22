import type { GridColDef } from '@mui/x-data-grid';

import { Label } from 'src/components/label';

export const AGENT_COLUMNS: GridColDef[] = [
  { field: 'name', headerName: 'Agent Name', flex: 1, minWidth: 200 },
  { field: 'version', headerName: 'Version', width: 120 },
  {
    field: 'status',
    headerName: 'Status',
    width: 150,
    renderCell: (params) => (
      <Label color={(params.value === 'Published' && 'success') || 'info'}>
        {params.value}
      </Label>
    ),
  },
  {
    field: 'createdAt',
    headerName: 'Created At',
    width: 200,
    valueGetter: (value) => new Date(value).toLocaleString(),
  },
];
