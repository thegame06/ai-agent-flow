import type { ClassificationResult, ExplanationData } from './types';

import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import IconButton from '@mui/material/IconButton';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';
import { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { BestMatchCard } from './BestMatchCard';
import { CandidatesListCard } from './CandidatesListCard';
import { ExplanationCard } from './ExplanationCard';

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
      setError(`${errorMsg}. Verifica que el backend esté corriendo en http://localhost:5183 y que existan intenciones configuradas.`);
    } finally {
      setLoading(false);
    }
  };

  const loadSample = (message: string) => {
    setTestMessage(message);
    setResult(null);
    setError(null);
  };

  const explanationData: ExplanationData | null = result
    ? JSON.parse(result.explanation_json)
    : null;

  return (
    <>
      <Helmet>
        <title>Intent Playground | AgentFlow</title>
      </Helmet>

      <DashboardContent maxWidth="lg">
        <Stack spacing={4}>
          {/* Header */}
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Stack spacing={1}>
              <Typography variant="h4">Intent Classification Playground</Typography>
              <Typography variant="body2" color="text.secondary">
                Test intent classification in real-time
              </Typography>
            </Stack>
            <Button
              variant="outlined"
              startIcon={<Iconify icon="eva:arrow-back-outline" />}
              href="/dashboard/intents"
            >
              Back to Intents
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
                  label="Test Message"
                  value={testMessage}
                  onChange={(e) => setTestMessage(e.target.value)}
                  placeholder="Type a message to classify... (e.g., 'Quiero solicitar un préstamo')"
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
                      Try:
                    </Typography>
                    {SAMPLE_MESSAGES.slice(0, 3).map((msg, index) => (
                      <Button
                        key={index}
                        size="small"
                        variant="text"
                        onClick={() => loadSample(msg)}
                        sx={{ fontSize: '0.75rem' }}
                      >
                        "{msg}"
                      </Button>
                    ))}
                  </Stack>

                  <Button
                    variant="contained"
                    onClick={handleClassify}
                    disabled={!testMessage.trim() || loading}
                    startIcon={loading ? <CircularProgress size={20} /> : <Iconify icon="eva:flash-fill" />}
                  >
                    {loading ? 'Classifying...' : 'Classify Intent'}
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
                <Typography variant="h6">Classification Results</Typography>
                <Typography variant="caption" color="text.disabled">
                  Processed in {result.processing_time_ms}ms
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
                    <Typography variant="subtitle2">How to use the Playground</Typography>
                  </Stack>
                  <Typography variant="body2" color="text.secondary">
                    1. Type or select a sample message above
                    <br />
                    2. Click "Classify Intent" to see how the system categorizes it
                    <br />
                    3. Review the best match, all candidates, and explanation for the decision
                    <br />
                    4. Use this to test and refine your intent definitions
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
