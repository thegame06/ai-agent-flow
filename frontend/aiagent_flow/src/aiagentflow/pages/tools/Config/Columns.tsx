import type { GridColDef } from '@mui/x-data-grid';

import { Label } from 'src/components/label';

export const TOOL_COLUMNS: GridColDef[] = [
  { field: 'name', headerName: 'Tool Name', width: 200 },
  { field: 'version', headerName: 'Version', width: 100 },
  { field: 'description', headerName: 'Description', flex: 1, minWidth: 300 },
  {
    field: 'riskLevel',
    headerName: 'Risk Level',
    width: 150,
    renderCell: (params) => (
      <Label color={
        (params.value === 'Critical' && 'error') ||
        (params.value === 'High' && 'warning') ||
        (params.value === 'Medium' && 'info') ||
        'success'
      }>
        {params.value}
      </Label>
    ),
  },
];
