import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import InputLabel from '@mui/material/InputLabel';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import CircularProgress from '@mui/material/CircularProgress';

import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { useSettingsWorkspace } from 'src/aiagentflow/pages/settings/SettingsWorkspaceContext';

type Person = {
  id: string;
  memberType: 'human' | 'virtual';
  displayName: string;
  roleTitle?: string;
  email?: string;
  phone?: string;
  agentId?: string;
  operationalRole?: string;
  active: boolean;
};

type QueueMember = { memberId: string; weight: number; capacity: number; active: boolean };
type Queue = {
  id: string;
  name: string;
  description?: string;
  assignmentStrategy: string;
  channels: string[];
  members: QueueMember[];
  active: boolean;
};

export default function WorkforcePage() {
  const { embedded } = useSettingsWorkspace();
  const tenantId = useTenantId();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [people, setPeople] = useState<Person[]>([]);
  const [queues, setQueues] = useState<Queue[]>([]);
  const [agents, setAgents] = useState<Array<{ id: string; name: string }>>([]);
  const [openPerson, setOpenPerson] = useState(false);
  const [openQueue, setOpenQueue] = useState(false);

  const [personForm, setPersonForm] = useState<Person>({
    id: '',
    memberType: 'human',
    displayName: '',
    roleTitle: '',
    email: '',
    phone: '',
    agentId: '',
    operationalRole: '',
    active: true,
  });

  const [queueForm, setQueueForm] = useState<Queue>({
    id: '',
    name: '',
    description: '',
    assignmentStrategy: 'least_load',
    channels: [],
    members: [],
    active: true,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError('');
      const [peopleRes, queueRes, agentsRes] = await Promise.all([
        axios.get(endpoints.agentflow.workforce.people(tenantId)),
        axios.get(endpoints.agentflow.workforce.queues(tenantId)),
        axios.get(endpoints.agentflow.agents.list(tenantId)),
      ]);
      setPeople(peopleRes.data || []);
      setQueues(queueRes.data || []);
      setAgents((agentsRes.data || []).map((a: any) => ({ id: a.id, name: a.name })));
    } catch (e: any) {
      setError(e?.message || 'No se pudo cargar configuración de equipos.');
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => { load(); }, [load]);

  const savePerson = async () => {
    await axios.post(endpoints.agentflow.workforce.people(tenantId), personForm);
    setOpenPerson(false);
    setPersonForm({ id: '', memberType: 'human', displayName: '', roleTitle: '', email: '', phone: '', agentId: '', operationalRole: '', active: true });
    await load();
  };

  const saveQueue = async () => {
    await axios.post(endpoints.agentflow.workforce.queues(tenantId), queueForm);
    setOpenQueue(false);
    setQueueForm({ id: '', name: '', description: '', assignmentStrategy: 'least_load', channels: [], members: [], active: true });
    await load();
  };

  return (
    <>
      <Helmet><title>Equipos y atención | AgentFlow</title></Helmet>
      <DashboardContent maxWidth="xl" disablePadding={embedded}>
        <Stack spacing={2.5}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Box>
              <Typography variant="h4">Equipos y atención</Typography>
              <Typography variant="body2" color="text.secondary">Define personas, asistentes virtuales y colas de escalación.</Typography>
            </Box>
            <Stack direction="row" spacing={1}>
              <Button variant="outlined" onClick={() => setOpenPerson(true)}>Nueva persona/virtual</Button>
              <Button variant="contained" onClick={() => setOpenQueue(true)}>Nuevo equipo/cola</Button>
            </Stack>
          </Stack>
          {error && <Alert severity="error">{error}</Alert>}
          {loading ? <Box sx={{ py: 8, textAlign: 'center' }}><CircularProgress /></Box> : (
            <Grid container spacing={2}>
              <Grid item xs={12} md={6}>
                <Card variant="outlined" sx={{ p: 2 }}>
                  <Typography variant="h6" sx={{ mb: 1.5 }}>Miembros</Typography>
                  <Table size="small">
                    <TableHead><TableRow><TableCell>Nombre</TableCell><TableCell>Tipo</TableCell><TableCell>Rol</TableCell><TableCell>Estado</TableCell></TableRow></TableHead>
                    <TableBody>
                      {people.map((p) => (
                        <TableRow key={p.id}>
                          <TableCell>{p.displayName}</TableCell>
                          <TableCell><Chip size="small" label={p.memberType === 'virtual' ? 'Virtual' : 'Humano'} /></TableCell>
                          <TableCell>{p.roleTitle || p.operationalRole || '-'}</TableCell>
                          <TableCell><Chip size="small" color={p.active ? 'success' : 'default'} label={p.active ? 'Activo' : 'Inactivo'} /></TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Card>
              </Grid>
              <Grid item xs={12} md={6}>
                <Card variant="outlined" sx={{ p: 2 }}>
                  <Typography variant="h6" sx={{ mb: 1.5 }}>Equipos / Colas</Typography>
                  <Table size="small">
                    <TableHead><TableRow><TableCell>Nombre</TableCell><TableCell>Estrategia</TableCell><TableCell>Miembros</TableCell><TableCell>Estado</TableCell></TableRow></TableHead>
                    <TableBody>
                      {queues.map((q) => (
                        <TableRow key={q.id}>
                          <TableCell>{q.name}</TableCell>
                          <TableCell>{q.assignmentStrategy}</TableCell>
                          <TableCell>{q.members.length}</TableCell>
                          <TableCell><Chip size="small" color={q.active ? 'success' : 'default'} label={q.active ? 'Activo' : 'Inactivo'} /></TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Card>
              </Grid>
            </Grid>
          )}
        </Stack>
      </DashboardContent>

      <Dialog open={openPerson} onClose={() => setOpenPerson(false)} fullWidth maxWidth="sm">
        <DialogTitle>Nuevo miembro</DialogTitle>
        <DialogContent>
          <Stack spacing={1.5} sx={{ pt: 1 }}>
            <TextField label="Nombre" value={personForm.displayName} onChange={(e) => setPersonForm((p) => ({ ...p, displayName: e.target.value }))} fullWidth />
            <FormControl fullWidth>
              <InputLabel>Tipo</InputLabel>
              <Select value={personForm.memberType} label="Tipo" onChange={(e) => setPersonForm((p) => ({ ...p, memberType: e.target.value as any }))}>
                <MenuItem value="human">Humano</MenuItem>
                <MenuItem value="virtual">Virtual (asistente AI)</MenuItem>
              </Select>
            </FormControl>
            {personForm.memberType === 'virtual' ? (
              <TextField select label="Agente virtual" value={personForm.agentId || ''} onChange={(e) => setPersonForm((p) => ({ ...p, agentId: e.target.value }))} fullWidth>
                {agents.map((a) => <MenuItem key={a.id} value={a.id}>{a.name}</MenuItem>)}
              </TextField>
            ) : (
              <>
                <TextField label="Cargo/Rol" value={personForm.roleTitle || ''} onChange={(e) => setPersonForm((p) => ({ ...p, roleTitle: e.target.value }))} fullWidth />
                <TextField label="Correo" value={personForm.email || ''} onChange={(e) => setPersonForm((p) => ({ ...p, email: e.target.value }))} fullWidth />
                <TextField label="Teléfono" value={personForm.phone || ''} onChange={(e) => setPersonForm((p) => ({ ...p, phone: e.target.value }))} fullWidth />
              </>
            )}
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setOpenPerson(false)}>Cancelar</Button><Button variant="contained" onClick={savePerson}>Guardar</Button></DialogActions>
      </Dialog>

      <Dialog open={openQueue} onClose={() => setOpenQueue(false)} fullWidth maxWidth="sm">
        <DialogTitle>Nueva cola/equipo</DialogTitle>
        <DialogContent>
          <Stack spacing={1.5} sx={{ pt: 1 }}>
            <TextField label="Nombre del equipo" value={queueForm.name} onChange={(e) => setQueueForm((q) => ({ ...q, name: e.target.value }))} fullWidth />
            <TextField label="Descripción" value={queueForm.description || ''} onChange={(e) => setQueueForm((q) => ({ ...q, description: e.target.value }))} fullWidth />
            <FormControl fullWidth>
              <InputLabel>Estrategia</InputLabel>
              <Select value={queueForm.assignmentStrategy} label="Estrategia" onChange={(e) => setQueueForm((q) => ({ ...q, assignmentStrategy: e.target.value }))}>
                <MenuItem value="least_load">least_load</MenuItem>
                <MenuItem value="round_robin">round_robin</MenuItem>
                <MenuItem value="skills_match">skills_match</MenuItem>
              </Select>
            </FormControl>
            <FormControl fullWidth>
              <InputLabel>Miembros</InputLabel>
              <Select
                multiple
                value={queueForm.members.map((m) => m.memberId)}
                label="Miembros"
                onChange={(e) => {
                  const ids = e.target.value as string[];
                  setQueueForm((q) => ({ ...q, members: ids.map((id) => ({ memberId: id, weight: 1, capacity: 10, active: true })) }));
                }}
              >
                {people.map((p) => <MenuItem key={p.id} value={p.id}>{p.displayName}</MenuItem>)}
              </Select>
            </FormControl>
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setOpenQueue(false)}>Cancelar</Button><Button variant="contained" onClick={saveQueue}>Guardar</Button></DialogActions>
      </Dialog>
    </>
  );
}
