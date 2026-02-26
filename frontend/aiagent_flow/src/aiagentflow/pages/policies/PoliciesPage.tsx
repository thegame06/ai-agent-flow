import type { GridColDef } from '@mui/x-data-grid';

import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import { DataGrid } from '@mui/x-data-grid';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';

// ----------------------------------------------------------------------

export default function PoliciesPage() {
  const tenantId = useTenantId();
  const [policies, setPolicies] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');

  const fetchPolicies = async () => {
    setLoading(true);
    try {
      const response = await axios.get(`/api/v1/tenants/${tenantId}/policies`);
      setPolicies(response.data);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchPolicies();
  }, [tenantId]);

  const createPolicySet = async () => {
    await axios.post(`/api/v1/tenants/${tenantId}/policies`, { name, description });
    setCreateOpen(false);
    setName('');
    setDescription('');
    await fetchPolicies();
  };

  const publishPolicySet = async (id: string) => {
    await axios.post(`/api/v1/tenants/${tenantId}/policies/${id}/publish`);
    await fetchPolicies();
  };

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
        <Label variant="soft" color={params.value === 'Published' ? 'success' : 'default'}>
          {params.value}
        </Label>
      ),
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: 180,
      sortable: false,
      renderCell: (params) => (
        <Button
          size="small"
          variant="outlined"
          disabled={params.row.status === 'Published'}
          onClick={() => publishPolicySet(params.row.id)}
        >
          Publish
        </Button>
      ),
    },
  ];

  return (
    <>
      <Helmet><title>Policies | {CONFIG.appName}</title></Helmet>
      <DashboardContent maxWidth="xl">
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
          <Typography variant="h4">Governance & Policies</Typography>
          <Button variant="contained" onClick={() => setCreateOpen(true)}>New Policy Set</Button>
        </Stack>

        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid rows={policies} columns={columns} loading={loading} getRowId={(row) => row.id} sx={{ border: 0 }} />
        </Card>

        <Dialog open={createOpen} onClose={() => setCreateOpen(false)} fullWidth maxWidth="sm">
          <DialogTitle>Create Policy Set</DialogTitle>
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} fullWidth />
              <TextField
                label="Description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                fullWidth
                multiline
                minRows={3}
              />
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button variant="contained" onClick={createPolicySet} disabled={!name.trim()}>Create</Button>
          </DialogActions>
        </Dialog>
      </DashboardContent>
    </>
  );
}
