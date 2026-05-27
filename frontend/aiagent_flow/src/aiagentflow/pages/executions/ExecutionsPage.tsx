import { useNavigate } from 'react-router';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import { DataGrid } from '@mui/x-data-grid';
import Typography from '@mui/material/Typography';
import { alpha, useTheme } from '@mui/material/styles';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { EXECUTION_COLUMNS } from './Config/Columns';
import { useExecutions } from './Hooks/useExecutions';

// ----------------------------------------------------------------------

export default function ExecutionsPage() {
  const theme = useTheme();
  const tenantId = useTenantId();
  const { executions, loading } = useExecutions(tenantId);
  const navigate = useNavigate();

  return (
    <>
      <Helmet>
        <title>Ejecuciones | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper
          variant="outlined"
          sx={{
            mb: 3,
            p: { xs: 2.5, md: 3 },
            borderRadius: 4,
            borderColor:
              theme.palette.mode === 'dark'
                ? alpha(theme.palette.primary.light, 0.22)
                : alpha(theme.palette.primary.main, 0.16),
            background:
              theme.palette.mode === 'dark'
                ? `radial-gradient(circle at 8% 18%, ${alpha(theme.palette.primary.main, 0.2)}, transparent 34%), linear-gradient(135deg, ${alpha(
                    theme.palette.background.paper,
                    0.96
                  )} 0%, ${alpha(theme.palette.grey[900], 0.9)} 100%)`
                : 'radial-gradient(circle at 8% 18%, rgba(14,124,90,0.14), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between" alignItems={{ md: 'center' }}>
            <Stack direction="row" spacing={1.5} alignItems="center">
              <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.lighter', color: 'primary.main' }}>
                <Iconify icon="mdi:chart-timeline-variant" width={30} />
              </Avatar>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Observabilidad
                </Typography>
                <Typography variant="h3">Ejecuciones</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                  Monitorea agentes, workflows y trazas de decision. Haz click en una fila para ver el detalle.
                </Typography>
              </Box>
            </Stack>
            <Chip label={`${executions.length} registros`} color="primary" variant="soft" />
          </Stack>
        </Paper>

        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid
            rows={executions}
            columns={EXECUTION_COLUMNS}
            loading={loading}
            getRowId={(row) => row.id}
            pageSizeOptions={[10, 25, 50]}
            onRowClick={(params) => {
              if (params.row.kind === 'agent') navigate(paths.dashboard.executionDetail(params.row.id));
            }}
            sx={{
              border: 0,
              '& .MuiDataGrid-row': { cursor: 'default' },
              '& .execution-row-agent': { cursor: 'pointer' },
            }}
            getRowClassName={(params) => `execution-row-${params.row.kind}`}
          />
        </Card>
      </DashboardContent>
    </>
  );
}
