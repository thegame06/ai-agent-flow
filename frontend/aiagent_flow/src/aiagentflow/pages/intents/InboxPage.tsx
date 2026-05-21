import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { InboxTable } from './InboxTable';
import { InboxFilters } from './InboxFilters';
import { InboxStatsCards } from './InboxStatsCards';

import type { InboxStats, InboxFilter, InboxConversation } from './types';

// ----------------------------------------------------------------------

export default function InboxPage() {
  const tenantId = useTenantId();
  const [conversations, setConversations] = useState<InboxConversation[]>([]);
  const [stats, setStats] = useState<InboxStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<InboxFilter>({ state: 'all', confidence: 'all' });
  const [intentKeys, setIntentKeys] = useState<string[]>([]);
  const [viewConversation, setViewConversation] = useState<InboxConversation | null>(null);
  const [reassignConversation, setReassignConversation] = useState<InboxConversation | null>(null);
  const [resolveConversation, setResolveConversation] = useState<InboxConversation | null>(null);
  const [newIntent, setNewIntent] = useState('');

  const normalizeState = (state: unknown) => (typeof state === 'string' ? state : String(state ?? 'Unknown'));
  const normalizeConfidence = (confidence: unknown) => (typeof confidence === 'string' ? confidence : String(confidence ?? 'Unknown'));

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [conversationsRes, statsRes] = await Promise.all([
        axios.get(endpoints.agentflow.intentRouting.conversations(tenantId)),
        axios.get(endpoints.agentflow.intentRouting.stats(tenantId)),
      ]);
      setConversations(conversationsRes.data || []);
      setStats(statsRes.data || null);
      const rulesRes = await axios.get(endpoints.agentflow.intentRouting.rules(tenantId));
      const keys: string[] = Array.from(
        new Set<string>(
          (rulesRes.data || [])
            .map((r: any) => String(r.intentKey || '').trim())
            .filter((value: string) => value.length > 0)
        )
      );
      setIntentKeys(keys);
    } catch (err) {
      console.error('Failed to load inbox data:', err);
      setError('Error al cargar inbox. Verifica que el backend este corriendo en http://localhost:5000');
      setConversations([]);
      setStats(null);
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleView = (conversation: InboxConversation) => {
    setViewConversation(conversation);
  };

  const handleReassign = async (conversationId: string, selectedIntent: string) => {
    try {
      await axios.post(
        endpoints.agentflow.intentRouting.conversationReassign(tenantId, conversationId),
        { newIntent: selectedIntent || null }
      );
      await loadData();
      setError(null);
    } catch (err) {
      console.error('Failed to reassign conversation:', err);
      setError('Error al reasignar conversacion. Verifica la conexion con el backend.');
    }
  };

  const handleResolve = async (conversationId: string) => {
    try {
      await axios.post(
        endpoints.agentflow.intentRouting.conversationResolve(tenantId, conversationId)
      );
      await loadData();
      setError(null);
    } catch (err) {
      console.error('Failed to resolve conversation:', err);
      setError('Error al resolver conversacion. Verifica la conexion con el backend.');
    }
  };

  // Filtering logic
  const filteredConversations = conversations.filter((conv) => {
    const state = normalizeState((conv as any).state);
    const confidence = normalizeConfidence((conv as any).confidence);
    // State filter
    if (filter.state !== 'all' && state !== filter.state) {
      return false;
    }

    // Confidence filter
    if (filter.confidence !== 'all' && confidence !== filter.confidence) {
      return false;
    }

    return true;
  });

  return (
    <>
      <Helmet>
        <title>Casos sin clasificar | AgentFlow</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack spacing={3}>
          {/* Header */}
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Stack spacing={1}>
              <Typography variant="h4">Casos sin clasificar</Typography>
              <Typography variant="body2" color="text.secondary">
                Revisa conversaciones que el sistema no pudo clasificar automaticamente
              </Typography>
            </Stack>
            <Stack direction="row" spacing={2}>
              <Button
                variant="outlined"
                startIcon={<Iconify icon="eva:refresh-outline" />}
                onClick={loadData}
              >
                Actualizar
              </Button>
              <Button
                variant="outlined"
                startIcon={<Iconify icon="eva:settings-2-outline" />}
              >
                Configuracion
              </Button>
            </Stack>
          </Stack>

          {/* Stats Cards */}
          <InboxStatsCards stats={stats} loading={loading} />

          {/* Error Alert */}
          {error && (
            <Alert severity="error" onClose={() => setError(null)}>
              {error}
            </Alert>
          )}

          {/* Filters */}
          <InboxFilters filter={filter} onChange={setFilter} />

          {/* Conversations Table */}
          <InboxTable
            conversations={filteredConversations}
            loading={loading}
            onView={handleView}
            onReassign={(conversationId) => {
              const conv = conversations.find((c) => c.id === conversationId) || null;
              setReassignConversation(conv);
              setNewIntent(conv?.detected_intent_key || '');
            }}
            onResolve={(conversationId) => {
              const conv = conversations.find((c) => c.id === conversationId) || null;
              setResolveConversation(conv);
            }}
          />
        </Stack>

        <Dialog open={Boolean(viewConversation)} onClose={() => setViewConversation(null)} maxWidth="sm" fullWidth>
          <DialogTitle>Detalle del caso</DialogTitle>
          <DialogContent>
            <Stack spacing={1.5} sx={{ pt: 1 }}>
              <Typography variant="body2"><b>ID:</b> {viewConversation?.id}</Typography>
              <Typography variant="body2"><b>Canal:</b> {viewConversation?.channel}</Typography>
              <Typography variant="body2"><b>Usuario:</b> {viewConversation?.user_identifier}</Typography>
              <Typography variant="body2"><b>Estado:</b> {normalizeState((viewConversation as any)?.state)}</Typography>
              <Typography variant="body2"><b>Confianza:</b> {normalizeConfidence((viewConversation as any)?.confidence)}</Typography>
              <Typography variant="body2"><b>Intencion detectada:</b> {viewConversation?.detected_intent_key || '-'}</Typography>
              <Typography variant="body2"><b>Creado:</b> {viewConversation?.created_at}</Typography>
              <Typography variant="body2"><b>Actualizado:</b> {viewConversation?.updated_at}</Typography>
              <Typography variant="subtitle2" sx={{ mt: 1 }}>Ultimo mensaje</Typography>
              <Alert severity="info" icon={false}>{viewConversation?.last_message || 'Sin mensaje'}</Alert>
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setViewConversation(null)}>Cerrar</Button>
          </DialogActions>
        </Dialog>

        <Dialog open={Boolean(reassignConversation)} onClose={() => setReassignConversation(null)} maxWidth="sm" fullWidth>
          <DialogTitle>Reclasificar caso</DialogTitle>
          <DialogContent>
            <Stack spacing={2} sx={{ pt: 1 }}>
              <Typography variant="body2" color="text.secondary">
                Selecciona la intencion correcta para marcar este caso como clasificado.
              </Typography>
              <TextField
                select
                fullWidth
                label="Intencion"
                value={newIntent}
                onChange={(e) => setNewIntent(e.target.value)}
              >
                <MenuItem value="">Sin intencion especifica</MenuItem>
                {intentKeys.map((key) => (
                  <MenuItem key={key} value={key}>{key}</MenuItem>
                ))}
              </TextField>
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setReassignConversation(null)}>Cancelar</Button>
            <Button
              variant="contained"
              onClick={async () => {
                if (!reassignConversation) return;
                await handleReassign(reassignConversation.id, newIntent);
                setReassignConversation(null);
              }}
            >
              Guardar clasificacion
            </Button>
          </DialogActions>
        </Dialog>

        <Dialog open={Boolean(resolveConversation)} onClose={() => setResolveConversation(null)} maxWidth="xs" fullWidth>
          <DialogTitle>Resolver caso</DialogTitle>
          <DialogContent>
            <Typography variant="body2" color="text.secondary" sx={{ pt: 1 }}>
              Confirmas marcar este caso como resuelto?
            </Typography>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setResolveConversation(null)}>Cancelar</Button>
            <Button
              color="success"
              variant="contained"
              onClick={async () => {
                if (!resolveConversation) return;
                await handleResolve(resolveConversation.id);
                setResolveConversation(null);
              }}
            >
              Marcar resuelto
            </Button>
          </DialogActions>
        </Dialog>
      </DashboardContent>
    </>
  );
}

