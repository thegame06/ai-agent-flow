import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';

import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { IntentsList } from './IntentsList';
import { IntentFilters } from './IntentFilters';
import { IntentSearchBar } from './IntentSearchBar';
import { CreateIntentDialog } from './CreateIntentDialog';

import type { Agent, Intent, Workflow, IntentFilter, IntentFormData } from './types';

// ----------------------------------------------------------------------

export default function IntentsPage() {
  const tenantId = useTenantId();
  const [intents, setIntents] = useState<Intent[]>([]);
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<IntentFilter>({ category: 'all', enabled: 'all' });
  const [searchQuery, setSearchQuery] = useState('');
  const [openDialog, setOpenDialog] = useState(false);
  const [selectedIntent, setSelectedIntent] = useState<Intent | null>(null);

  const mapRuleToIntent = useCallback((rule: any): Intent => ({
    id: rule.id,
    key: rule.intentKey ?? '',
    name: rule.intentKey ?? '',
    description: rule.intentDescription ?? '',
    category: 'General',
    examples: rule.examplePhrases ?? [],
    synonyms: [],
    confidence_threshold: 0.7,
    priority: rule.priority ?? 100,
    workflow_id: rule.workflowDefinitionId ?? '',
    workflow_name: rule.workflowName ?? '',
    target_agent_id: rule.targetAgentId ?? '',
    enabled: rule.enabled ?? true,
    is_base_intent: false,
    created_at: rule.createdAt ?? '',
    updated_at: rule.updatedAt ?? '',
  }), []);

  const loadIntents = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      
      const [intentsRes, workflowsRes, agentsRes] = await Promise.all([
        axios.get(endpoints.agentflow.intentRouting.rules(tenantId)),
        axios.get(endpoints.agentflow.workflows.list(tenantId)).catch(() => ({ data: [] })),
        axios.get(endpoints.agentflow.agents.list(tenantId)).catch(() => ({ data: [] })),
      ]);
      
      setIntents((intentsRes.data || []).map(mapRuleToIntent));
      setWorkflows(workflowsRes.data || []);
      setAgents(agentsRes.data || []);
    } catch (err) {
      console.error('Failed to load intents:', err);
      setError('Error al cargar intenciones. Verifica que el backend esté corriendo en http://localhost:5000');
      setIntents([]);
    } finally {
      setLoading(false);
    }
  }, [mapRuleToIntent, tenantId]);

  useEffect(() => {
    loadIntents();
  }, [loadIntents]);

  const handleEdit = (intent: Intent) => {
    setSelectedIntent(intent);
    setOpenDialog(true);
  };

  const handleToggle = async (intentId: string, enabled: boolean) => {
    try {
      await axios.post(endpoints.agentflow.intentRouting.ruleEnable(tenantId, intentId), {
        enabled,
      });
      setIntents((prev) =>
        prev.map((i) => (i.id === intentId ? { ...i, enabled } : i))
      );
      setError(null);
    } catch (err) {
      console.error('Failed to toggle intent:', err);
      setError(`Error al ${enabled ? 'activar' : 'desactivar'} intención. Verifica la conexión con el backend.`);
    }
  };

  const handleDelete = async (intentId: string) => {
    if (!window.confirm('Are you sure you want to delete this intent?')) return;

    try {
      await axios.delete(endpoints.agentflow.intentRouting.ruleById(tenantId, intentId));
      setIntents((prev) => prev.filter((i) => i.id !== intentId));
      setError(null);
    } catch (err) {
      console.error('Failed to delete intent:', err);
      setError('Error al eliminar intención. Verifica la conexión con el backend.');
    }
  };

  const handleSave = async (data: IntentFormData) => {
    try {
      const sourceAgentId = data.target_agent_id || agents[0]?.id || 'router';
      const payload = {
        intentKey: data.key,
        intentDescription: data.description,
        examplePhrases: data.examples,
        sourceAgentId,
        targetAgentId: data.target_agent_id || sourceAgentId,
        workflowDefinitionId: data.workflow_id || null,
        workflowName: workflows.find((wf) => wf.id === data.workflow_id)?.name ?? null,
        priority: data.priority,
        enabled: data.enabled,
      };

      if (selectedIntent) {
        await axios.put(
          endpoints.agentflow.intentRouting.ruleById(tenantId, selectedIntent.id),
          payload
        );
      } else {
        await axios.post(endpoints.agentflow.intentRouting.rules(tenantId), payload);
      }
      await loadIntents();
      setOpenDialog(false);
      setSelectedIntent(null);
      setError(null);
    } catch (err) {
      console.error('Failed to save intent:', err);
      setError(`Error al ${selectedIntent ? 'actualizar' : 'crear'} intención. Verifica los datos y la conexión con el backend.`);
    }
  };

  // Filtering logic
  const filteredIntents = intents.filter((intent) => {
    // Category filter
    if (filter.category !== 'all' && intent.category !== filter.category) {
      return false;
    }

    // Enabled filter
    if (filter.enabled === 'enabled' && !intent.enabled) return false;
    if (filter.enabled === 'disabled' && intent.enabled) return false;

    // Search filter
    if (searchQuery) {
      const query = searchQuery.toLowerCase();
      return (
        intent.name.toLowerCase().includes(query) ||
        intent.key.toLowerCase().includes(query) ||
        intent.description.toLowerCase().includes(query)
      );
    }

    return true;
  });

  return (
    <>
      <Helmet>
        <title>Reglas de Intención | AgentFlow</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack spacing={3}>
          {/* Header */}
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Stack spacing={1}>
              <Typography variant="h4">Reglas de Intención</Typography>
              <Typography variant="body2" color="text.secondary">
                Configura las reglas de routing para clasificar automáticamente los mensajes entrantes
              </Typography>
            </Stack>
            <Stack direction="row" spacing={2}>
              <Button
                variant="outlined"
                startIcon={<Iconify icon="eva:play-circle-outline" />}
                href="/dashboard/intents/playground"
              >
                Probar clasificación
              </Button>
              <Button
                variant="contained"
                startIcon={<Iconify icon="eva:plus-fill" />}
                onClick={() => {
                  setSelectedIntent(null);
                  setOpenDialog(true);
                }}
              >
                Nueva regla
              </Button>
            </Stack>
          </Stack>

          {/* Filters */}
          <IntentFilters filter={filter} onChange={setFilter} />

          {/* Error Alert */}
          {error && (
            <Alert severity="error" onClose={() => setError(null)}>
              {error}
            </Alert>
          )}

          {/* Search */}
          <IntentSearchBar value={searchQuery} onChange={setSearchQuery} />

          {/* Intents Table */}
          <IntentsList
            intents={filteredIntents}
            onEdit={handleEdit}
            onToggle={handleToggle}
            onDelete={handleDelete}
            loading={loading}
          />
        </Stack>

        {/* Create/Edit Dialog */}
        <CreateIntentDialog
          open={openDialog}
          intent={selectedIntent}
          workflows={workflows}
          agents={agents}
          onClose={() => {
            setOpenDialog(false);
            setSelectedIntent(null);
          }}
          onSave={handleSave}
        />
      </DashboardContent>
    </>
  );
}
