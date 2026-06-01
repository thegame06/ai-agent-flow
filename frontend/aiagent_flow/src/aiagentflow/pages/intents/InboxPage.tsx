import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Divider from '@mui/material/Divider';
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
  const [viewConversationDetail, setViewConversationDetail] = useState<any | null>(null);
  const [viewConversationJourney, setViewConversationJourney] = useState<any | null>(null);
  const [viewConversationSession, setViewConversationSession] = useState<any | null>(null);
  const [viewConversationMessages, setViewConversationMessages] = useState<any[]>([]);
  const [viewConversationLoading, setViewConversationLoading] = useState(false);
  const [reassignConversation, setReassignConversation] = useState<InboxConversation | null>(null);
  const [resolveConversation, setResolveConversation] = useState<InboxConversation | null>(null);
  const [newIntent, setNewIntent] = useState('');

  const getUserIdentifier = (conversation?: InboxConversation | null) =>
    conversation?.userIdentifier || conversation?.user_identifier || '';
  const getLastMessage = (conversation?: InboxConversation | null) =>
    conversation?.lastMessage || conversation?.last_message || '';
  const getDetectedIntentKey = (conversation?: InboxConversation | null) =>
    conversation?.detectedIntentKey || conversation?.detected_intent_key || '';
  const getAssignedAgentId = (conversation?: InboxConversation | null) =>
    conversation?.assignedAgentId || conversation?.assigned_agent_id || '';
  const getWorkflowExecutionId = (conversation?: InboxConversation | null) =>
    conversation?.workflowExecutionId || conversation?.workflow_execution_id || '';
  const getReviewNotes = (conversation?: InboxConversation | null) =>
    conversation?.reviewNotes || conversation?.review_notes || '';
  const getCreatedAt = (conversation?: InboxConversation | null) =>
    conversation?.createdAt || conversation?.created_at || '';
  const getUpdatedAt = (conversation?: InboxConversation | null) =>
    conversation?.updatedAt || conversation?.updated_at || '';

  const conversationEntries = (() => {
    if (viewConversationJourney?.timeline?.length) {
      return viewConversationJourney.timeline
        .filter((item: any) => item.category === 'customer_message' || item.category === 'reply')
        .map((item: any) => ({
          id: item.id,
          actor: item.category === 'customer_message' ? 'Cliente' : 'Sistema',
          occurredAt: item.occurredAt,
          content: item.description || item.detail || item.title,
        }));
    }

    if (viewConversationMessages?.length) {
      return viewConversationMessages.map((item: any) => ({
        id: item.id,
        actor:
          item.direction === 'Incoming'
            ? 'Cliente'
            : item.actor?.startsWith('agent:')
              ? item.actor.replace('agent:', 'Agente ')
              : item.actor || 'Sistema',
        occurredAt: item.createdAt,
        content: item.content,
      }));
    }

    return [];
  })();

  const normalizeState = (state: unknown) => (typeof state === 'string' ? state : String(state ?? 'Unknown'));
  const normalizeConfidence = (confidence: unknown) => (typeof confidence === 'string' ? confidence : String(confidence ?? 'Unknown'));
  const toDate = (value?: string) => {
    if (!value) return '-';
    const dt = new Date(value);
    if (Number.isNaN(dt.getTime())) return '-';
    return dt.toLocaleString();
  };
  const stateLabel = (state: string) => {
    const map: Record<string, string> = {
      AwaitingClassification: 'Esperando clasificacion',
      Classified: 'Clasificado',
      LowConfidence: 'Baja confianza',
      NoMatch: 'Sin coincidencia',
      InProgress: 'En progreso',
      PendingHumanReview: 'Revision humana',
      Resolved: 'Resuelto',
      Escalated: 'Escalado',
      ConflictDetected: 'Conflicto',
      Abandoned: 'Abandonado',
      Unknown: 'Desconocido',
      '0': 'Esperando clasificacion',
      '1': 'Clasificado',
      '2': 'Baja confianza',
      '3': 'Sin coincidencia',
    };
    return map[state] ?? state;
  };
  const confidenceLabel = (confidence: string) => {
    const map: Record<string, string> = {
      High: 'Alta',
      Medium: 'Media',
      Low: 'Baja',
      NoMatch: 'Sin match',
      Unknown: 'Desconocida',
      '0': 'Sin match',
      '1': 'Baja',
      '2': 'Media',
      '3': 'Alta',
    };
    return map[confidence] ?? confidence;
  };

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [conversationsRes, statsRes] = await Promise.all([
        axios.get(endpoints.agentflow.inbox.list(tenantId)),
        axios.get(endpoints.agentflow.inbox.stats(tenantId)),
      ]);
      setConversations(conversationsRes.data?.items || []);
      const statsPayload = statsRes.data || {};
      setStats({
        total: statsPayload.totalConversations ?? statsPayload.total ?? 0,
        awaiting_classification: statsPayload.awaitingClassification ?? statsPayload.awaiting_classification ?? 0,
        classified: statsPayload.classified ?? statsPayload.byState?.Classified ?? 0,
        in_progress: statsPayload.inProgress ?? statsPayload.in_progress ?? 0,
        resolved_today: statsPayload.resolvedToday ?? statsPayload.resolved_today ?? 0,
        avg_confidence: statsPayload.avgConfidence ?? statsPayload.avg_confidence ?? 0,
        requires_review: statsPayload.requiresReview ?? statsPayload.requires_review ?? 0,
      });
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

  const handleView = async (conversation: InboxConversation) => {
    setViewConversation(conversation);
    setViewConversationLoading(true);
    try {
      const [detailRes, journeyRes] = await Promise.allSettled([
        axios.get(endpoints.agentflow.inbox.detail(tenantId, conversation.id)),
        axios.get(endpoints.agentflow.audit.journey(tenantId, conversation.id)),
      ]);

      setViewConversationDetail(detailRes.status === 'fulfilled' ? detailRes.value.data : conversation);
      setViewConversationJourney(journeyRes.status === 'fulfilled' ? journeyRes.value.data : null);

      const [sessionRes, messagesRes] = await Promise.allSettled([
        axios.get(endpoints.agentflow.channelSessions.detail(tenantId, conversation.id)),
        axios.get(endpoints.agentflow.channelSessions.messages(tenantId, conversation.id)),
      ]);

      setViewConversationSession(sessionRes.status === 'fulfilled' ? sessionRes.value.data : null);
      setViewConversationMessages(
        messagesRes.status === 'fulfilled'
          ? (messagesRes.value.data?.items || messagesRes.value.data || [])
          : []
      );
    } catch (err) {
      console.error('Failed to load conversation detail:', err);
      setViewConversationDetail(conversation);
      setViewConversationJourney(null);
      setViewConversationSession(null);
      setViewConversationMessages([]);
    } finally {
      setViewConversationLoading(false);
    }
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

        <Dialog
          open={Boolean(viewConversation)}
          onClose={() => {
            setViewConversation(null);
            setViewConversationDetail(null);
            setViewConversationJourney(null);
            setViewConversationSession(null);
            setViewConversationMessages([]);
          }}
          maxWidth="md"
          fullWidth
        >
          <DialogTitle>Detalle del caso</DialogTitle>
          <DialogContent>
            <Stack spacing={2} sx={{ pt: 1 }}>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Chip size="small" label={`Canal: ${viewConversationDetail?.channel || viewConversation?.channel || '-'}`} />
                <Chip size="small" color="info" label={`Estado: ${stateLabel(normalizeState(viewConversationDetail?.state ?? (viewConversation as any)?.state))}`} />
                <Chip size="small" color="warning" label={`Confianza: ${confidenceLabel(normalizeConfidence(viewConversationDetail?.confidence ?? (viewConversation as any)?.confidence))}`} />
              </Stack>

              <Paper variant="outlined" sx={{ p: 2 }}>
                <Stack spacing={1.25}>
                  <Typography variant="body2"><b>ID:</b> {viewConversationDetail?.id || viewConversation?.id || '-'}</Typography>
                  <Typography variant="body2"><b>Usuario:</b> {getUserIdentifier(viewConversationDetail) || getUserIdentifier(viewConversation) || viewConversationSession?.identifier || viewConversationJourney?.summary?.customer?.identifier || '-'}</Typography>
                  <Typography variant="body2"><b>Intencion detectada:</b> {getDetectedIntentKey(viewConversationDetail) || getDetectedIntentKey(viewConversation) || 'Sin intencion detectada'}</Typography>
                  <Typography variant="body2"><b>Agente asignado:</b> {getAssignedAgentId(viewConversationDetail) || getAssignedAgentId(viewConversation) || viewConversationSession?.agentId || '-'}</Typography>
                  <Typography variant="body2"><b>Ejecucion de workflow:</b> {getWorkflowExecutionId(viewConversationDetail) || getWorkflowExecutionId(viewConversation) || '-'}</Typography>
                  <Typography variant="body2"><b>Nota del sistema:</b> {getReviewNotes(viewConversationDetail) || getReviewNotes(viewConversation) || '-'}</Typography>
                  <Typography variant="body2"><b>Creado:</b> {toDate(getCreatedAt(viewConversationDetail) || getCreatedAt(viewConversation) || viewConversationJourney?.summary?.startedAt)}</Typography>
                  <Typography variant="body2"><b>Actualizado:</b> {toDate(getUpdatedAt(viewConversationDetail) || getUpdatedAt(viewConversation) || viewConversationSession?.lastActivityAt || viewConversationJourney?.summary?.lastUpdatedAt)}</Typography>
                </Stack>
              </Paper>

              <Divider />
              <Stack spacing={1}>
                <Typography variant="subtitle2">Ultimo mensaje del cliente</Typography>
                <Paper
                  variant="outlined"
                  sx={{
                    p: 1.5,
                    bgcolor: 'background.neutral',
                    borderColor: 'divider',
                  }}
                >
                  <Typography variant="body2">
                    {getLastMessage(viewConversationDetail)?.trim()
                      || getLastMessage(viewConversation)?.trim()
                      || viewConversationJourney?.summary?.firstCustomerMessage?.trim()
                      || 'Sin mensaje'}
                  </Typography>
                </Paper>
              </Stack>

              <Stack spacing={1}>
                <Typography variant="subtitle2">Conversacion</Typography>
                <Paper variant="outlined" sx={{ p: 1.5, maxHeight: 320, overflow: 'auto' }}>
                  <Stack spacing={1.25}>
                    {viewConversationLoading && <Typography variant="body2" color="text.secondary">Cargando detalle...</Typography>}
                    {!viewConversationLoading && conversationEntries.length ? (
                      conversationEntries.map((item: any) => (
                        <Stack key={item.id} spacing={0.25}>
                          <Typography variant="caption" color="text.secondary">
                            {item.actor} · {toDate(item.occurredAt)}
                          </Typography>
                          <Typography variant="body2">{item.content}</Typography>
                        </Stack>
                      ))
                    ) : !viewConversationLoading ? (
                      <Typography variant="body2" color="text.secondary">No hay detalle conversacional disponible.</Typography>
                    ) : null}
                  </Stack>
                </Paper>
              </Stack>
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => {
              setViewConversation(null);
              setViewConversationDetail(null);
              setViewConversationJourney(null);
              setViewConversationSession(null);
              setViewConversationMessages([]);
            }}>Cerrar</Button>
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

