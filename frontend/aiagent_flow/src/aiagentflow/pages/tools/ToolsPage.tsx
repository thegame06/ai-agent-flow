import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import { DataGrid } from '@mui/x-data-grid';
import Typography from '@mui/material/Typography';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { useTools } from './Hooks/useTools';
import { TOOL_COLUMNS } from './Config/Columns';

// ----------------------------------------------------------------------

export default function ToolsPage() {
  const { tools, loading } = useTools();

  return (
    <>
      <Helmet>
        <title>Extensions & Tools | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Platform Tools & Extensions</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            Registered capabilities available for your agents.
          </Typography>
        </Box>

        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid
            rows={tools}
            columns={TOOL_COLUMNS}
            loading={loading}
            getRowId={(row) => row.name + row.version}
            pageSizeOptions={[10, 25, 50]}
            sx={{ border: 0 }}
          />
        </Card>
      </DashboardContent>
    </>
  );
}
