import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Card from '@mui/material/Card';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';

import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { BestMatchCard } from './BestMatchCard';
import { ExplanationCard } from './ExplanationCard';
import { CandidatesListCard } from './CandidatesListCard';

import type { ExplanationData, ClassificationResult } from './types';

// ----------------------------------------------------------------------

const SAMPLE_MESSAGES = [
  'Quiero solicitar un préstamo personal',
  'What is my account balance?',
  'My payment was declined',
  'I need help with my credit card',
  'How do I open a savings account?',
];

export default function PlaygroundPage() {
  const tenantId = useTenantId();
  const [testMessage, setTestMessage] = useState('');
  const [result, setResult] = useState<ClassificationResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleClassify = async () => {
    if (!testMessage.trim()) return;

    setLoading(true);
    setError(null);
    setResult(null);
    
    try {
      const res = await axios.post(endpoints.agentflow.intentRouting.classify(tenantId), {
        message: testMessage,
        channel: 'whatsapp',
      });
      setResult(res.data);
    } catch (err: any) {
      console.error('Classification failed:', err);
      const errorMsg = err?.response?.data?.message || err?.message || 'Error al clasificar mensaje';
      setError(`${errorMsg}. Verifica que el backend esté corriendo en http://localhost:5000 y que existan intenciones configuradas.`);
    } finally {
      setLoading(false);
    }
  };

  const loadSample = (message: string) => {
    setTestMessage(message);
    setResult(null);
    setError(null);
  };

  const explanationData: ExplanationData | null = (() => {
    if (!result?.explanation_json) return null;
    try {
      const parsed = JSON.parse(result.explanation_json);
      return {
        decision: parsed?.decision || parsed?.message || 'Clasificación completada.',
        factors: Array.isArray(parsed?.factors) ? parsed.factors : [],
        alternatives_considered: Number(parsed?.alternatives_considered ?? parsed?.candidates_count ?? 0),
      };
    } catch {
      return {
        decision: 'No se pudo interpretar la explicación detallada.',
        factors: [],
        alternatives_considered: 0,
      };
    }
  })();

  return (
    <>
      <Helmet>
        <title>Prueba de Clasificación | AgentFlow</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        <Stack spacing={4}>
          {/* Header */}
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Stack spacing={1}>
              <Typography variant="h4">Prueba de Clasificación</Typography>
              <Typography variant="body2" color="text.secondary">
                Prueba cómo el sistema clasifica mensajes en tiempo real
              </Typography>
            </Stack>
            <Button
              variant="outlined"
              startIcon={<Iconify icon="eva:arrow-back-outline" />}
              href="/dashboard/intents"
            >
              Volver a reglas
            </Button>
          </Stack>

          {/* Test Input Card */}
          <Card>
            <CardContent>
              <Stack spacing={3}>
                {/* Error Alert */}
                {error && (
                  <Alert severity="error" onClose={() => setError(null)}>
                    {error}
                  </Alert>
                )}

                <TextField
                  fullWidth
                  multiline
                  rows={4}
                  label="Mensaje de prueba"
                  value={testMessage}
                  onChange={(e) => setTestMessage(e.target.value)}
                  placeholder="Escribe un mensaje para clasificar (ej: 'Quiero solicitar un préstamo')"
                  onKeyPress={(e) => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      handleClassify();
                    }
                  }}
                />

                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Stack direction="row" spacing={1}>
                    <Typography variant="caption" color="text.disabled">
                      Prueba con:
                    </Typography>
                    {SAMPLE_MESSAGES.slice(0, 3).map((msg, index) => (
                      <Button
                        key={index}
                        size="small"
                        variant="text"
                        onClick={() => loadSample(msg)}
                        sx={{ fontSize: '0.75rem' }}
                      >
                        &quot;{msg}&quot;
                      </Button>
                    ))}
                  </Stack>

                  <Button
                    variant="contained"
                    onClick={handleClassify}
                    disabled={!testMessage.trim() || loading}
                    startIcon={loading ? <CircularProgress size={20} /> : <Iconify icon="eva:flash-fill" />}
                  >
                    {loading ? 'Clasificando...' : 'Clasificar mensaje'}
                  </Button>
                </Stack>
              </Stack>
            </CardContent>
          </Card>

          {/* Error */}
          {error && (
            <Card sx={{ bgcolor: 'error.lighter', borderColor: 'error.main' }}>
              <CardContent>
                <Stack direction="row" spacing={1} alignItems="center">
                  <Iconify icon="eva:alert-circle-fill" color="error.main" />
                  <Typography color="error.main">{error}</Typography>
                </Stack>
              </CardContent>
            </Card>
          )}

          {/* Results */}
          {result && (
            <Stack spacing={3}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="h6">Resultados de clasificación</Typography>
                <Typography variant="caption" color="text.disabled">
                  Procesado en {result.processing_time_ms}ms
                </Typography>
              </Stack>

              <BestMatchCard
                match={result.best_match}
                confidence={result.best_score}
                confidenceLevel={result.confidence}
              />

              <CandidatesListCard candidates={result.all_candidates} />

              {explanationData && <ExplanationCard explanation={explanationData} />}
            </Stack>
          )}

          {/* Help Text */}
          {!result && !loading && (
            <Card sx={{ bgcolor: 'background.neutral' }}>
              <CardContent>
                <Stack spacing={2}>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Iconify icon="eva:bulb-outline" width={24} />
                    <Typography variant="subtitle2">Cómo usar esta herramienta</Typography>
                  </Stack>
                  <Typography variant="body2" color="text.secondary">
                    1. Escribe o selecciona un mensaje de prueba arriba
                    <br />
                    2. Haz clic en &quot;Clasificar mensaje&quot; para ver cómo lo categoriza el sistema
                    <br />
                    3. Revisa la mejor coincidencia, todos los candidatos y la explicación de la decisión
                    <br />
                    4. Usa esto para probar y refinar tus reglas de intención
                  </Typography>
                </Stack>
              </CardContent>
            </Card>
          )}
        </Stack>
      </DashboardContent>
    </>
  );
}
