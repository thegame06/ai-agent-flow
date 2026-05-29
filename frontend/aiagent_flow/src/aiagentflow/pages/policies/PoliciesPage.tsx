import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

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

  const fetchPolicies = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await axios.get(`/api/v1/tenants/${tenantId}/policies`);
      setPolicies(response.data);
    } catch (e: any) {
      setError(e?.message || 'No se pudieron cargar las reglas de control.');
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    void fetchPolicies();
  }, [fetchPolicies]);

  const createPolicySet = async () => {
    try {
      setError(null);
      await axios.post(`/api/v1/tenants/${tenantId}/policies`, { name, description });
      setCreateOpen(false);
      setName('');
      setDescription('');
      await fetchPolicies();
    } catch (e: any) {
      setError(e?.message || 'No se pudo crear el conjunto de reglas.');
    }
  };

  const publishPolicySet = async (id: string) => {
    try {
      setError(null);
      await axios.post(`/api/v1/tenants/${tenantId}/policies/${id}/publish`);
      await fetchPolicies();
    } catch (e: any) {
      setError(e?.message || 'No se pudo publicar la version.');
    }
  };

  const clonePolicyVersion = async (id: string) => {
    try {
      setError(null);
      await axios.post(`/api/v1/tenants/${tenantId}/policies/${id}/clone-version`);
      await fetchPolicies();
    } catch (e: any) {
      setError(e?.message || 'No se pudo crear una nueva version.');
    }
  };

  const openEditRules = async (id: string) => {
    try {
      setError(null);
      const res = await axios.get(`/api/v1/tenants/${tenantId}/policies/${id}`);
      setEditing(res.data);
      setRulesJson(JSON.stringify(res.data.policies ?? [], null, 2));
      setEditOpen(true);
    } catch (e: any) {
      setError(e?.message || 'No se pudo cargar el detalle de reglas.');
    }
  };

  const saveRules = async () => {
    if (!editing) return;

    try {
      setError(null);
      const parsed = JSON.parse(rulesJson);
      await axios.put(`/api/v1/tenants/${tenantId}/policies/${editing.id}/policies`, {
        policies: parsed,
      });
      setEditOpen(false);
      setEditing(null);
      await fetchPolicies();
    } catch (e: any) {
      if (e instanceof SyntaxError) {
        setError('El JSON de reglas no es valido. Corrigelo antes de guardar.');
        return;
      }
      setError(e?.message || 'No se pudieron guardar las reglas.');
    }
  };

  const columns = [
    { field: 'name', headerName: 'Conjunto', flex: 1, minWidth: 220 },
    { field: 'version', headerName: 'Version', width: 120 },
    {
      field: 'status',
      headerName: 'Estado',
      width: 150,
      renderCell: (params: any) => (
        <Label variant="soft" color={params.value === 'Published' ? 'success' : 'default'}>
          {params.value === 'Published' ? 'Publicada' : params.value}
        </Label>
      ),
    },
    { field: 'policyCount', headerName: 'Reglas', width: 120 },
    {
      field: 'severity',
      headerName: 'Severidad',
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
          {params.value === 'Critical'
            ? 'Critica'
            : params.value === 'High'
              ? 'Alta'
              : params.value === 'Medium'
                ? 'Media'
                : params.value}
        </Label>
      ),
    },
    {
      field: 'actions',
      headerName: 'Acciones',
      width: 260,
      sortable: false,
      renderCell: (params: any) => (
        <Stack direction="row" spacing={1}>
          <Button
            size="small"
            variant="outlined"
            disabled={params.row.status === 'Published'}
            onClick={() => void publishPolicySet(params.row.id)}
          >
            Publicar
          </Button>
          <Button size="small" onClick={() => void openEditRules(params.row.id)}>
            Editar reglas
          </Button>
          {params.row.status === 'Published' && (
            <Button
              size="small"
              variant="text"
              onClick={() => void clonePolicyVersion(params.row.id)}
            >
              Nueva version
            </Button>
          )}
        </Stack>
      ),
    },
  ];

  return (
    <>
      <Helmet>
        <title>Reglas de control | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
          <Stack spacing={0.5}>
            <Typography variant="h4">Reglas de control</Typography>
            <Typography variant="body2" color="text.secondary">
              Define versiones publicables de reglas operativas, riesgo y validacion para el tenant.
            </Typography>
          </Stack>
          <Stack direction="row" spacing={1}>
            <IconButton onClick={() => void fetchPolicies()}>
              <Iconify icon="mdi:refresh" />
            </IconButton>
            <Button variant="contained" onClick={() => setCreateOpen(true)}>
              Nuevo conjunto
            </Button>
          </Stack>
        </Stack>

        {error && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button color="inherit" size="small" onClick={() => void fetchPolicies()}>Reintentar</Button>}
          >
            {error}
          </Alert>
        )}

        <Card sx={{ height: 600, width: '100%' }}>
          <DataGrid
            rows={policies}
            columns={columns}
            loading={loading}
            getRowId={(row) => row.id}
            sx={{ border: 0 }}
          />
        </Card>

        <Dialog open={createOpen} onClose={() => setCreateOpen(false)} fullWidth maxWidth="sm">
          <DialogTitle>Nuevo conjunto de reglas</DialogTitle>
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label="Nombre" value={name} onChange={(e) => setName(e.target.value)} fullWidth />
              <TextField
                label="Descripcion"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                fullWidth
                multiline
                minRows={3}
              />
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setCreateOpen(false)}>Cancelar</Button>
            <Button variant="contained" onClick={() => void createPolicySet()} disabled={!name.trim()}>
              Crear
            </Button>
          </DialogActions>
        </Dialog>

        <Dialog open={editOpen} onClose={() => setEditOpen(false)} fullWidth maxWidth="md">
          <DialogTitle>Editar reglas - {editing?.name}</DialogTitle>
          <DialogContent>
            <TextField
              label="JSON de reglas"
              value={rulesJson}
              onChange={(e) => setRulesJson(e.target.value)}
              fullWidth
              multiline
              minRows={18}
              sx={{ mt: 1 }}
            />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setEditOpen(false)}>Cancelar</Button>
            <Button variant="contained" onClick={() => void saveRules()}>
              Guardar reglas
            </Button>
          </DialogActions>
        </Dialog>
      </DashboardContent>
    </>
  );
}
