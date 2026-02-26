import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import { DataGrid } from '@mui/x-data-grid';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';

export default function PoliciesPage() {
  const tenantId = useTenantId();
  const [policies, setPolicies] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');

  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState<any | null>(null);
  const [rulesJson, setRulesJson] = useState('[]');

  const fetchPolicies = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await axios.get(`/api/v1/tenants/${tenantId}/policies`);
      setPolicies(response.data);
    } catch (e: any) {
      setError(e?.message || 'Failed to load policies');
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

  const openEditRules = async (id: string) => {
    const res = await axios.get(`/api/v1/tenants/${tenantId}/policies/${id}`);
    setEditing(res.data);
    setRulesJson(JSON.stringify(res.data.policies ?? [], null, 2));
    setEditOpen(true);
  };

  const saveRules = async () => {
    if (!editing) return;
    const parsed = JSON.parse(rulesJson);
    await axios.put(`/api/v1/tenants/${tenantId}/policies/${editing.id}/policies`, {
      policies: parsed,
    });
    setEditOpen(false);
    setEditing(null);
    await fetchPolicies();
  };

  const columns = [
    { field: 'name', headerName: 'Policy Set', flex: 1, minWidth: 220 },
    { field: 'version', headerName: 'Version', width: 120 },
    {
      field: 'status',
      headerName: 'Status',
      width: 150,
      renderCell: (params: any) => (
        <Label variant="soft" color={params.value === 'Published' ? 'success' : 'default'}>
          {params.value}
        </Label>
      ),
    },
    { field: 'policyCount', headerName: 'Policies', width: 120 },
    {
      field: 'severity',
      headerName: 'Severity',
      width: 130,
      renderCell: (params: any) => (
        <Label
          variant="soft"
          color={
            params.value === 'Critical'
              ? 'error'
              : params.value === 'High'
                ? 'warning'
                : 'info'
          }
        >
          {params.value}
        </Label>
      ),
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: 220,
      sortable: false,
      renderCell: (params: any) => (
        <Stack direction="row" spacing={1}>
          <Button
            size="small"
            variant="outlined"
            disabled={params.row.status === 'Published'}
            onClick={() => publishPolicySet(params.row.id)}
          >
            Publish
          </Button>
          <Button size="small" onClick={() => openEditRules(params.row.id)}>
            Edit Rules
          </Button>
        </Stack>
      ),
    },
  ];

  return (
    <>
      <Helmet>
        <title>Policies | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
          <Typography variant="h4">Governance & Policies</Typography>
          <Stack direction="row" spacing={1}>
            <IconButton onClick={fetchPolicies}><Iconify icon="mdi:refresh" /></IconButton>
            <Button variant="contained" onClick={() => setCreateOpen(true)}>New Policy Set</Button>
          </Stack>
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} action={<Button color="inherit" size="small" onClick={fetchPolicies}>Retry</Button>}>
            {error}
          </Alert>
        )}

        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid rows={policies} columns={columns} loading={loading} getRowId={(row) => row.id} sx={{ border: 0 }} />
        </Card>

        <Dialog open={createOpen} onClose={() => setCreateOpen(false)} fullWidth maxWidth="sm">
          <DialogTitle>Create Policy Set</DialogTitle>
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} fullWidth />
              <TextField label="Description" value={description} onChange={(e) => setDescription(e.target.value)} fullWidth multiline minRows={3} />
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button variant="contained" onClick={createPolicySet} disabled={!name.trim()}>Create</Button>
          </DialogActions>
        </Dialog>

        <Dialog open={editOpen} onClose={() => setEditOpen(false)} fullWidth maxWidth="md">
          <DialogTitle>Edit Policy Rules - {editing?.name}</DialogTitle>
          <DialogContent>
            <TextField
              label="Policies JSON"
              value={rulesJson}
              onChange={(e) => setRulesJson(e.target.value)}
              fullWidth
              multiline
              minRows={18}
              sx={{ mt: 1 }}
            />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setEditOpen(false)}>Cancel</Button>
            <Button variant="contained" onClick={saveRules}>Save Rules</Button>
          </DialogActions>
        </Dialog>
      </DashboardContent>
    </>
  );
}
