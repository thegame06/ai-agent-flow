import { Helmet } from 'react-helmet-async';
import { useCallback, useEffect, useState } from 'react';

import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { InboxFilters } from './InboxFilters';
import { InboxStatsCards } from './InboxStatsCards';
import { InboxTable } from './InboxTable';

import type { InboxConversation, InboxFilter, InboxStats } from './types';

// ----------------------------------------------------------------------

export default function InboxPage() {
  const tenantId = useTenantId();
  const [conversations, setConversations] = useState<InboxConversation[]>([]);
  const [stats, setStats] = useState<InboxStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<InboxFilter>({ state: 'all', confidence: 'all' });

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
    } catch (err) {
      console.error('Failed to load inbox data:', err);
      setError('Error al cargar inbox. Verifica que el backend esté corriendo en http://localhost:5000');
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
    console.log('View conversation:', conversation.id);
    // TODO: Navigate to conversation detail or open dialog
  };

  const handleReassign = async (conversationId: string) => {
    try {
      await axios.post(
        endpoints.agentflow.intentRouting.conversationReassign(tenantId, conversationId),
        { new_intent: 'other' } // This would come from a dialog
      );
      await loadData();
      setError(null);
    } catch (err) {
      console.error('Failed to reassign conversation:', err);
      setError('Error al reasignar conversación. Verifica la conexión con el backend.');
    }
  };

  const handleResolve = async (conversationId: string) => {
    if (!window.confirm('Mark this conversation as resolved?')) return;

    try {
      await axios.post(
        endpoints.agentflow.intentRouting.conversationResolve(tenantId, conversationId)
      );
      await loadData();
      setError(null);
    } catch (err) {
      console.error('Failed to resolve conversation:', err);
      setError('Error al resolver conversación. Verifica la conexión con el backend.');
    }
  };

  // Filtering logic
  const filteredConversations = conversations.filter((conv) => {
    // State filter
    if (filter.state !== 'all' && conv.state !== filter.state) {
      return false;
    }

    // Confidence filter
    if (filter.confidence !== 'all' && conv.confidence !== filter.confidence) {
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
                Revisa conversaciones que el sistema no pudo clasificar automáticamente
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
                Configuración
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
            onReassign={handleReassign}
            onResolve={handleResolve}
          />
        </Stack>
      </DashboardContent>
    </>
  );
}
