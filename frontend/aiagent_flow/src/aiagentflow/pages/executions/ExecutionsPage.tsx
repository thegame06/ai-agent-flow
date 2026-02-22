import { useNavigate } from 'react-router';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import { DataGrid } from '@mui/x-data-grid';
import Typography from '@mui/material/Typography';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { EXECUTION_COLUMNS } from './Config/Columns';
import { useExecutions } from './Hooks/useExecutions';

// ----------------------------------------------------------------------

export default function ExecutionsPage() {
  const { executions, loading } = useExecutions('tenant-1');
  const navigate = useNavigate();

  return (
    <>
      <Helmet>
        <title>Executions | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Execution History</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            Monitor and audit all agent executions across the platform. Click a row to view the full decision trace.
          </Typography>
        </Box>

        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid
            rows={executions}
            columns={EXECUTION_COLUMNS}
            loading={loading}
            getRowId={(row) => row.id}
            pageSizeOptions={[10, 25, 50]}
            onRowClick={(params) => navigate(paths.dashboard.executionDetail(params.row.id))}
            sx={{
              border: 0,
              '& .MuiDataGrid-row': { cursor: 'pointer' },
            }}
          />
        </Card>
      </DashboardContent>
    </>
  );
}
