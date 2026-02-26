import type { GridColDef } from '@mui/x-data-grid';

import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import { DataGrid } from '@mui/x-data-grid';
import Typography from '@mui/material/Typography';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';

// ----------------------------------------------------------------------

export default function PoliciesPage() {
  const tenantId = useTenantId();
  const [policies, setPolicies] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetch = async () => {
      try {
        const response = await axios.get(`/api/v1/tenants/${tenantId}/policies`);
        setPolicies(response.data);
      } finally {
        setLoading(false);
      }
    };
    fetch();
  }, [tenantId]);

  const columns: GridColDef[] = [
    { field: 'name', headerName: 'Policy Name', width: 200 },
    { field: 'description', headerName: 'Description', flex: 1 },
    {
      field: 'severity',
      headerName: 'Severity',
      width: 120,
      renderCell: (params) => (
        <Label color={(params.value === 'Critical' && 'error') || (params.value === 'High' && 'warning') || 'info'}>
          {params.value}
        </Label>
      ),
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 150,
      renderCell: (params) => (
        <Label variant="soft" color={params.value === 'Enabled' ? 'success' : 'default'}>
          {params.value}
        </Label>
      ),
    },
  ];

  return (
    <>
      <Helmet><title>Policies | {CONFIG.appName}</title></Helmet>
      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Governance & Policies</Typography>
        </Box>
        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid rows={policies} columns={columns} loading={loading} getRowId={(row) => row.id} sx={{ border: 0 }} />
        </Card>
      </DashboardContent>
    </>
  );
}
