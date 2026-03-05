import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import { DataGrid, GridToolbar } from '@mui/x-data-grid';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { useEvaluations } from './Hooks/useEvaluations';
import { evaluationsColumns, pendingReviewColumns } from './Config/Columns';

// ----------------------------------------------------------------------

export default function EvaluationsPage() {
  const tenantId = useTenantId();
  const { evaluations, pendingReview, summaries, loading, error, loadEvaluations, loadPendingReview } = useEvaluations(tenantId);

  const [activeTab, setActiveTab] = useState<'all' | 'pending' | 'summaries'>('all');

  const handleRefresh = async () => {
    if (activeTab === 'pending') {
      await loadPendingReview();
    } else {
      await loadEvaluations(undefined, 100);
    }
  };

  const renderStats = () => {
    const totalEvaluations = summaries.reduce((acc, s) => acc + s.totalEvaluations, 0);
    const avgScore = summaries.length > 0
      ? summaries.reduce((acc, s) => acc + s.averageScore, 0) / summaries.length
      : 0;
    const avgPassRate = summaries.length > 0
      ? summaries.reduce((acc, s) => acc + s.passRate, 0) / summaries.length
      : 0;

    return (
      <Grid container spacing={3} sx={{ mb: 3 }}>
        <Grid item xs={12} sm={4}>
          <Card sx={{ p: 3, textAlign: 'center' }}>
            <Typography variant="h4" color="primary">{totalEvaluations}</Typography>
            <Typography variant="body2" color="text.secondary">Total Evaluations</Typography>
          </Card>
        </Grid>
        <Grid item xs={12} sm={4}>
          <Card sx={{ p: 3, textAlign: 'center' }}>
            <Typography variant="h4" color="success.main">{avgScore.toFixed(1)}%</Typography>
            <Typography variant="body2" color="text.secondary">Average Score</Typography>
          </Card>
        </Grid>
        <Grid item xs={12} sm={4}>
          <Card sx={{ p: 3, textAlign: 'center' }}>
            <Typography variant="h4" color="info.main">{avgPassRate.toFixed(1)}%</Typography>
            <Typography variant="body2" color="text.secondary">Average Pass Rate</Typography>
          </Card>
        </Grid>
      </Grid>
    );
  };

  const renderPendingReview = () => (
    <Card sx={{ p: 2 }}>
      <Box sx={{ mb: 2, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="h6">Pending Review ({pendingReview.length})</Typography>
        <Button size="small" onClick={handleRefresh}>
          <Iconify icon="solar:refresh-line-duotone" sx={{ mr: 0.5 }} /> Refresh
        </Button>
      </Box>
      {pendingReview.length === 0 ? (
        <Alert severity="success">No evaluations pending review!</Alert>
      ) : (
        <Box sx={{ height: 400 }}>
          <DataGrid
            rows={pendingReview}
            columns={pendingReviewColumns}
            loading={loading}
            getRowId={(row) => row.id}
            slots={{ toolbar: GridToolbar }}
            initialState={{
              pagination: { paginationModel: { pageSize: 10 } },
              sorting: { sortModel: [{ field: 'createdAt', sort: 'asc' }] },
            }}
            pageSizeOptions={[10, 20, 50]}
            disableRowSelectionOnClick
          />
        </Box>
      )}
    </Card>
  );

  const renderAllEvaluations = () => (
    <Card sx={{ p: 2 }}>
      <Box sx={{ mb: 2, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="h6">All Evaluations</Typography>
        <Button size="small" onClick={handleRefresh}>
          <Iconify icon="solar:refresh-line-duotone" sx={{ mr: 0.5 }} /> Refresh
        </Button>
      </Box>
      <Box sx={{ height: 600 }}>
        <DataGrid
          rows={evaluations}
          columns={evaluationsColumns}
          loading={loading}
          getRowId={(row) => row.id}
          slots={{ toolbar: GridToolbar }}
          slotProps={{
            toolbar: {
              showQuickFilter: true,
              quickFilterProps: { placeholder: 'Search evaluations...' },
            },
          }}
          initialState={{
            pagination: { paginationModel: { pageSize: 20 } },
            sorting: { sortModel: [{ field: 'createdAt', sort: 'desc' }] },
          }}
          pageSizeOptions={[10, 20, 50, 100]}
          disableRowSelectionOnClick
        />
      </Box>
    </Card>
  );

  return (
    <>
      <Helmet>
        <title>Evaluations | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Typography variant="h4">Evaluations Dashboard</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Monitor agent quality, champion/challenger comparisons, and review pending evaluations.
          </Typography>
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 3 }}>
            {error}
          </Alert>
        )}

        {/* Stats */}
        {renderStats()}

        {/* Tabs */}
        <Stack direction="row" spacing={2} sx={{ mb: 3 }}>
          <Button
            variant={activeTab === 'all' ? 'contained' : 'outlined'}
            onClick={() => setActiveTab('all')}
          >
            All Evaluations
          </Button>
          <Button
            variant={activeTab === 'pending' ? 'contained' : 'outlined'}
            onClick={() => setActiveTab('pending')}
            startIcon={pendingReview.length > 0 && <Iconify icon="solar:bell-bing-bold-duotone" />}
          >
            Pending Review ({pendingReview.length})
          </Button>
          <Button
            variant={activeTab === 'summaries' ? 'contained' : 'outlined'}
            onClick={() => setActiveTab('summaries')}
          >
            Agent Summaries
          </Button>
        </Stack>

        {/* Content */}
        {activeTab === 'pending' && renderPendingReview()}
        {activeTab === 'all' && renderAllEvaluations()}
        {activeTab === 'summaries' && (
          <Alert severity="info">Agent summaries view coming soon. Use the evaluations list to analyze individual runs.</Alert>
        )}
      </DashboardContent>
    </>
  );
}
