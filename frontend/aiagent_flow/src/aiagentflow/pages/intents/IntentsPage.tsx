import type { Intent, IntentFormData, IntentFilter } from './types';

import { useState, useEffect, useCallback } from 'react';
import { Helmet } from 'react-helmet-async';

import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import Box from '@mui/material/Box';

import axios from 'src/lib/axios';
import { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { IntentsList } from './IntentsList';
import { IntentFilters } from './IntentFilters';
import { IntentSearchBar } from './IntentSearchBar';
import { CreateIntentDialog } from './CreateIntentDialog';

// ----------------------------------------------------------------------

export default function IntentsPage() {
  const tenantId = useTenantId();
  const [intents, setIntents] = useState<Intent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<IntentFilter>({ category: 'all', enabled: 'all' });
  const [searchQuery, setSearchQuery] = useState('');
  const [openDialog, setOpenDialog] = useState(false);
  const [selectedIntent, setSelectedIntent] = useState<Intent | null>(null);

  const loadIntents = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await axios.get(endpoints.agentflow.intentRouting.rules(tenantId));
      setIntents(res.data || []);
    } catch (error) {
      console.error('Failed to load intents:', error);
      setError('Error al cargar intenciones. Verifica que el backend esté corriendo en http://localhost:5183');
      setIntents([]);
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

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
    } catch (error) {
      console.error('Failed to toggle intent:', error);
      setError(`Error al ${enabled ? 'activar' : 'desactivar'} intención. Verifica la conexión con el backend.`);
    }
  };

  const handleDelete = async (intentId: string) => {
    if (!window.confirm('Are you sure you want to delete this intent?')) return;

    try {
      await axios.delete(endpoints.agentflow.intentRouting.ruleById(tenantId, intentId));
      setIntents((prev) => prev.filter((i) => i.id !== intentId));
      setError(null);
    } catch (error) {
      console.error('Failed to delete intent:', error);
      setError('Error al eliminar intención. Verifica la conexión con el backend.');
    }
  };

  const handleSave = async (data: IntentFormData) => {
    try {
      if (selectedIntent) {
        // Update existing
        await axios.put(
          endpoints.agentflow.intentRouting.ruleById(tenantId, selectedIntent.id),
          data
        );
      } else {
        // Create new
        await axios.post(endpoints.agentflow.intentRouting.rules(tenantId), data);
      }
      await loadIntents();
      setOpenDialog(false);
      setSelectedIntent(null);
      setError(null);
    } catch (error) {
      console.error('Failed to save intent:', error);
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
        <title>Intent Management | AgentFlow</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack spacing={3}>
          {/* Header */}
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Stack spacing={1}>
              <Typography variant="h4">Intent Management</Typography>
              <Typography variant="body2" color="text.secondary">
                Manage and configure intent routing rules for conversations
              </Typography>
            </Stack>
            <Stack direction="row" spacing={2}>
              <Button
                variant="outlined"
                startIcon={<Iconify icon="eva:play-circle-outline" />}
                href="/dashboard/intents/playground"
              >
                Playground
              </Button>
              <Button
                variant="contained"
                startIcon={<Iconify icon="eva:plus-fill" />}
                onClick={() => {
                  setSelectedIntent(null);
                  setOpenDialog(true);
                }}
              >
                Create Intent
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
