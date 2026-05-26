import { useState } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';

import axios, { endpoints } from 'src/lib/axios';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { ChatWizardHost, type ChatWizardMessage } from './ChatWizardHost';

type WizardQuestion = {
  question: string;
  multiSelect: boolean;
  options: Array<{ label: string; description: string }>;
};

type WizardMode = 'voice' | 'text' | 'video_voice';

export function OutboundVoiceAssistantWizard() {
  const tenantId = useTenantId();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [ok, setOk] = useState('');
  const [sessionId, setSessionId] = useState('');
  const [stage, setStage] = useState('');
  const [completed, setCompleted] = useState(false);
  const [question, setQuestion] = useState<WizardQuestion | null>(null);
  const [artifact, setArtifact] = useState<Record<string, string>>({});
  const [assistantPayload, setAssistantPayload] = useState<any>(null);
  const [createdAgentId, setCreatedAgentId] = useState('');
  const [wizardMode, setWizardMode] = useState<WizardMode>('voice');
  const [messages, setMessages] = useState<ChatWizardMessage[]>([
    {
      role: 'assistant',
      content:
        'Este wizard crea un asistente outbound de voz preguntando una cosa por turno. Haz clic en iniciar para comenzar.',
    },
  ]);
  const [input, setInput] = useState('');

  const initialize = async () => {
    try {
      setLoading(true);
      setError('');
      setOk('');
      setAssistantPayload(null);
      setCreatedAgentId('');
      const res = await axios.post(endpoints.agentflow.assistant.wizardCreateSession, { tenantId, mode: wizardMode });
      const data = res.data;
      setSessionId(data.sessionId);
      setStage(data.stage);
      setCompleted(Boolean(data.completed));
      setQuestion(data.question ?? null);
      setArtifact(data.artifact ?? {});
      setMessages((prev) => [...prev, { role: 'assistant', content: data.question?.question ?? 'Sesion iniciada.' }]);
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo iniciar wizard.');
    } finally {
      setLoading(false);
    }
  };

  const refresh = async () => {
    if (!sessionId) return;
    try {
      setLoading(true);
      const res = await axios.get(endpoints.agentflow.assistant.wizardSession(sessionId));
      const data = res.data;
      setStage(data.stage);
      setCompleted(Boolean(data.completed));
      setQuestion(data.question ?? null);
      setArtifact(data.artifact ?? {});
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo consultar sesion.');
    } finally {
      setLoading(false);
    }
  };

  const sendAnswer = async (answer: string) => {
    if (!sessionId || !answer.trim() || loading) return;
    try {
      setLoading(true);
      setError('');
      setMessages((prev) => [...prev, { role: 'user', content: answer }]);
      const res = await axios.post(endpoints.agentflow.assistant.wizardAnswer(sessionId), { answer });
      const data = res.data;
      setStage(data.stage);
      setCompleted(Boolean(data.completed));
      setQuestion(data.question ?? null);
      setArtifact(data.artifact ?? {});
      if (data.question?.question) {
        setMessages((prev) => [...prev, { role: 'assistant', content: data.question.question }]);
      } else if (data.completed) {
        setMessages((prev) => [...prev, { role: 'assistant', content: 'Configuracion completada. Materializa para ver el payload final.' }]);
      }
    } catch (e: any) {
      const msg = e?.data?.error ?? e?.message ?? 'No se pudo responder.';
      setError(msg);
      setMessages((prev) => [...prev, { role: 'assistant', content: `Error: ${msg}` }]);
    } finally {
      setLoading(false);
    }
  };

  const materialize = async () => {
    if (!sessionId || loading) return;
    try {
      setLoading(true);
      setError('');
      const res = await axios.post(endpoints.agentflow.assistant.wizardMaterialize(sessionId));
      setAssistantPayload(res.data?.assistant ?? null);
      setOk('Asistente materializado correctamente.');
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo materializar.');
    } finally {
      setLoading(false);
    }
  };

  const createVoiceAgent = async () => {
    if (!assistantPayload || loading) return;
    try {
      setLoading(true);
      setError('');
      setOk('');
      const payload = mapAssistantToAgentDesignerPayload(assistantPayload, artifact, wizardMode);
      const res = await axios.post(endpoints.agentflow.agents.create(tenantId), payload);
      const agentId = res?.data?.id ?? '';
      setCreatedAgentId(agentId);
      setOk(agentId ? `Agente creado: ${agentId}` : 'Agente creado correctamente.');
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo crear el agente.');
    } finally {
      setLoading(false);
    }
  };

  const onSend = async () => {
    const value = input.trim();
    setInput('');
    await sendAnswer(value);
  };

  return (
    <Stack spacing={2}>
      {error && <Alert severity="error">{error}</Alert>}
      {ok && <Alert severity="success">{ok}</Alert>}

      <ChatWizardHost
        title="Wizard outbound de voz"
        subtitle="Flujo one-question-at-a-time conectado al backend."
        messages={messages}
        inputValue={input}
        inputPlaceholder={question?.question ? 'Escribe exactamente una opcion...' : 'Inicia la sesion...'}
        onInputChange={setInput}
        onSend={onSend}
        loading={loading}
        sendDisabled={!sessionId || completed || !input.trim()}
      >
        <Box sx={{ p: 1.25, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
          <Stack spacing={1}>
            <Stack direction="row" spacing={1}>
              <Chip label="Text" color={wizardMode === 'text' ? 'primary' : 'default'} onClick={() => setWizardMode('text')} />
              <Chip label="Voice" color={wizardMode === 'voice' ? 'primary' : 'default'} onClick={() => setWizardMode('voice')} />
              <Chip label="Multimodal" color={wizardMode === 'video_voice' ? 'primary' : 'default'} onClick={() => setWizardMode('video_voice')} />
            </Stack>
            <Stack direction="row" spacing={1}>
              <Button variant="contained" onClick={initialize} disabled={loading}>
                Iniciar wizard
              </Button>
              <Button variant="outlined" onClick={refresh} disabled={loading || !sessionId}>
                Refrescar
              </Button>
              <Button variant="outlined" onClick={materialize} disabled={loading || !sessionId || !completed}>
                Materializar
              </Button>
              <Button variant="outlined" onClick={createVoiceAgent} disabled={loading || !assistantPayload}>
                Crear agente voice
              </Button>
            </Stack>
            <Typography variant="caption" color="text.secondary">
              Session: {sessionId || '-'} | Stage: {stage || '-'} | Completed: {completed ? 'si' : 'no'}
            </Typography>

            {question && (
              <Stack spacing={0.5}>
                <Typography variant="caption" color="text.secondary">
                  Opciones validas:
                </Typography>
                <Stack direction="row" spacing={0.75} flexWrap="wrap">
                  {question.options.map((opt) => (
                    <Chip key={opt.label} label={opt.label} onClick={() => sendAnswer(opt.label)} />
                  ))}
                </Stack>
              </Stack>
            )}

            {Object.keys(artifact).length > 0 && (
              <Box sx={{ p: 1, borderRadius: 1, bgcolor: 'background.neutral' }}>
                <Typography variant="caption" color="text.secondary">
                  Artifact:
                </Typography>
                {Object.entries(artifact).map(([k, v]) => (
                  <Typography key={k} variant="body2">
                    {k}: {v}
                  </Typography>
                ))}
              </Box>
            )}

            {assistantPayload && (
              <Box sx={{ p: 1, borderRadius: 1, border: '1px solid', borderColor: 'divider', maxHeight: 240, overflow: 'auto' }}>
                <Typography variant="caption" color="text.secondary">
                  Assistant payload (POST /assistant):
                </Typography>
                <pre style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{JSON.stringify(assistantPayload, null, 2)}</pre>
              </Box>
            )}

            {createdAgentId && (
              <Typography variant="body2">
                Agente disponible en: /dashboard/agents/{createdAgentId}
              </Typography>
            )}
          </Stack>
        </Box>
      </ChatWizardHost>
    </Stack>
  );
}

function mapAssistantToAgentDesignerPayload(assistant: any, artifact: Record<string, string>, wizardMode: WizardMode) {
  const task = artifact.Task || 'Seguimiento outbound';
  const tone = artifact.Tone || 'Amigable';
  const language = artifact.Language || 'Spanish';
  const provider = normalizeProviderName(assistant?.reasoning?.provider);
  const primaryModel = assistant?.reasoning?.model || 'claude-haiku-4-5-20251001';
  const maxTokens = Number(assistant?.reasoning?.maxTokens ?? 250);

  return {
    name: assistant?.name || `${task} (Voice)`,
    description: `${task} - ${tone} - ${language}`,
    status: 'Draft',
    version: '1.0.0',
    brain: {
      primaryModel,
      fallbackModel: '',
      reasoningModelCandidatesCsv: '',
      provider,
      systemPrompt: buildSystemPrompt(artifact),
      temperature: 0.4,
      maxResponseTokens: Number.isFinite(maxTokens) ? Math.min(Math.max(maxTokens, 128), 4096) : 512,
    },
    loop: {
      maxSteps: 25,
      timeoutPerStepMs: 30000,
      maxTokensPerExecution: 100000,
      maxRetries: 2,
      enablePromptInjectionGuard: true,
      enablePIIProtection: true,
      requireHumanApproval: false,
      humanApprovalThreshold: 'high_risk',
      allowParallelToolCalls: false,
      plannerType: 'ReAct',
      runtimeMode: 'Autonomous',
    },
    memory: {
      workingMemory: true,
      longTermMemory: false,
      vectorMemory: false,
      auditMemory: true,
    },
    session: {
      runtimeKind: mapWizardModeToRuntimeKind(assistant?.channel, wizardMode),
      enableThreads: true,
      defaultThreadTtlHours: 168,
      maxTurnsPerThread: 100,
      contextWindowSize: 20,
      autoCreateThread: true,
      enableSummarization: true,
      threadKeyPattern: '{agentName}-{guid}',
    },
    steps: [
      {
        id: 'voice-think',
        type: 'think',
        label: 'Descubrir contexto',
        description: 'Analiza intención y etapa comercial.',
        config: {},
        position: { x: 0, y: 0 },
        connections: ['voice-act'],
      },
      {
        id: 'voice-act',
        type: 'act',
        label: 'Avanzar siguiente paso',
        description: 'Propone siguiente paso concreto sin repreguntas redundantes.',
        config: {},
        position: { x: 0, y: 100 },
        connections: ['voice-observe'],
      },
      {
        id: 'voice-observe',
        type: 'observe',
        label: 'Validar continuidad',
        description: 'Confirma continuidad y cierre del turno.',
        config: {},
        position: { x: 0, y: 200 },
        connections: [],
      },
    ],
    tools: [],
    tags: ['wizard', 'outbound', 'voice'],
  };
}

function mapWizardModeToRuntimeKind(channel: string | undefined, mode: WizardMode): 'Text' | 'Voice' | 'MultimodalRealtime' {
  const normalized = (channel || mode).toLowerCase();
  if (normalized === 'text') return 'Text';
  if (normalized === 'video_voice' || normalized === 'video-voice' || normalized === 'multimodal') return 'MultimodalRealtime';
  return 'Voice';
}

function normalizeProviderName(provider: string | undefined): string {
  const value = (provider || '').trim().toLowerCase();
  if (!value) return 'Anthropic';
  if (value === 'openai') return 'OpenAI';
  if (value === 'anthropic') return 'Anthropic';
  return provider || 'Anthropic';
}

function buildSystemPrompt(artifact: Record<string, string>): string {
  const language = artifact.Language || 'Spanish';
  const task = artifact.Task || 'Seguimiento de leads';
  const audience = artifact.Callers || 'Prospectos en negociación';
  const tone = artifact.Tone || 'Amigable';
  return [
    `Eres un agente outbound de voz en ${language}.`,
    `Objetivo: ${task}.`,
    `Audiencia: ${audience}.`,
    `Tono: ${tone}.`,
    'Haz una pregunta por turno y evita repreguntar datos ya confirmados.',
    'Si hay interés, cierra con un siguiente paso concreto y verificable.',
  ].join('\n');
}
