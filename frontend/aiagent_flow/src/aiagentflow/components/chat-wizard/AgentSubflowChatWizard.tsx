import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import axios, { endpoints } from 'src/lib/axios';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { useWizardProgress } from './useWizardProgress';
import { ChatWizardHost, type ChatWizardMessage } from './ChatWizardHost';

type AgentListItem = { id: string; name: string; status: string };
type TemplateId = 'sales' | 'support' | 'collection';
type StepState = 'agent' | 'template' | 'preview' | 'apply';

type AgentStep = {
  id: string;
  type: string;
  label: string;
  description: string;
  config: Record<string, unknown>;
  position: { x: number; y: number };
  connections: string[];
};

type AgentDetailDto = {
  name: string;
  description: string;
  tags: string[];
  brain: any;
  loop: any;
  memory: any;
  session: any;
  tools: any[];
};

type WizardState = {
  messages: ChatWizardMessage[];
  input: string;
  stepState: StepState;
  agentId: string;
  templateId: TemplateId;
  draftSteps: AgentStep[];
};

const initialState: WizardState = {
  messages: [{ role: 'assistant', content: 'Vamos a configurar un sub-flujo de agente. Primero selecciona el agente objetivo.' }],
  input: '',
  stepState: 'agent',
  agentId: '',
  templateId: 'sales',
  draftSteps: [],
};

function buildTemplateSteps(templateId: TemplateId): AgentStep[] {
  if (templateId === 'support') {
    return [
      { id: 'support-think-issue', type: 'think', label: 'Diagnosticar caso', description: 'Analizar motivo y contexto del problema.', config: { mode: 'diagnostic' }, position: { x: 80, y: 80 }, connections: ['support-act-questions'] },
      { id: 'support-act-questions', type: 'act', label: 'Solicitar informacion clave', description: 'Pedir solo los datos minimos para resolver.', config: { style: 'guided_questions' }, position: { x: 300, y: 80 }, connections: ['support-decide-resolution'] },
      { id: 'support-decide-resolution', type: 'decide', label: 'Definir resolucion', description: 'Seleccionar accion o escalamiento.', config: { output: 'resolution_plan' }, position: { x: 520, y: 80 }, connections: [] },
    ];
  }
  if (templateId === 'collection') {
    return [
      { id: 'collection-think-balance', type: 'think', label: 'Validar saldo', description: 'Revisar estado de cuenta y monto pendiente.', config: { mode: 'payment_validation' }, position: { x: 80, y: 80 }, connections: ['collection-act-options'] },
      { id: 'collection-act-options', type: 'act', label: 'Ofrecer opciones', description: 'Explicar opciones de pago o convenio.', config: { style: 'payment_options' }, position: { x: 300, y: 80 }, connections: ['collection-aggregate-result'] },
      { id: 'collection-aggregate-result', type: 'aggregate', label: 'Confirmar acuerdo', description: 'Registrar opcion elegida y proximos pasos.', config: { output: 'payment_commitment' }, position: { x: 520, y: 80 }, connections: [] },
    ];
  }
  return [
    { id: 'sales-think-needs', type: 'think', label: 'Entender necesidad', description: 'Identificar intencion de compra y contexto.', config: { mode: 'qualification' }, position: { x: 80, y: 80 }, connections: ['sales-act-collect'] },
    { id: 'sales-act-collect', type: 'act', label: 'Recolectar datos', description: 'Solicitar datos clave para cotizar.', config: { style: 'guided_dialog' }, position: { x: 300, y: 80 }, connections: ['sales-aggregate-offer'] },
    { id: 'sales-aggregate-offer', type: 'aggregate', label: 'Construir propuesta', description: 'Preparar resumen y siguiente accion de cierre.', config: { output: 'sales_offer' }, position: { x: 520, y: 80 }, connections: [] },
  ];
}

export function AgentSubflowChatWizard() {
  const tenantId = useTenantId();
  const { state: saved, setState: setSaved, reset } = useWizardProgress<WizardState>(tenantId, 'agentSubflow', initialState);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [ok, setOk] = useState('');
  const [messages, setMessages] = useState<ChatWizardMessage[]>(saved.messages);
  const [input, setInput] = useState(saved.input);
  const [stepState, setStepState] = useState<StepState>(saved.stepState);
  const [agents, setAgents] = useState<AgentListItem[]>([]);
  const [agentId, setAgentId] = useState(saved.agentId);
  const [templateId, setTemplateId] = useState<TemplateId>(saved.templateId);
  const [draftSteps, setDraftSteps] = useState<AgentStep[]>(saved.draftSteps);

  const selectedAgent = useMemo(() => agents.find((a) => a.id === agentId) ?? null, [agentId, agents]);

  useEffect(() => {
    const loadAgents = async () => {
      setLoading(true);
      try {
        const res = await axios.get(endpoints.agentflow.agents.list(tenantId));
        const list = (res.data ?? []) as AgentListItem[];
        const active = list.filter((item) => item.status !== 'Archived');
        setAgents(active);
        if (!agentId && active.length > 0) setAgentId(active[0].id);
      } catch (e: any) {
        setError(e?.message ?? 'No se pudieron cargar asistentes.');
      } finally {
        setLoading(false);
      }
    };
    loadAgents();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  useEffect(() => {
    setSaved({ messages, input, stepState, agentId, templateId, draftSteps });
  }, [agentId, draftSteps, input, messages, setSaved, stepState, templateId]);

  const next = (nextState: StepState, assistantMessage: string) => {
    setStepState(nextState);
    setMessages((prev) => [...prev, { role: 'assistant', content: assistantMessage }]);
  };

  const onSend = () => {
    const text = input.trim();
    if (!text || loading) return;
    setInput('');
    setMessages((prev) => [...prev, { role: 'user', content: text }]);
  };

  const confirmAgent = () => {
    if (!selectedAgent) return;
    next('template', `Excelente. Trabajaremos sobre "${selectedAgent.name}". Elige una plantilla de sub-flujo.`);
  };

  const confirmTemplate = () => {
    const generated = buildTemplateSteps(templateId);
    setDraftSteps(generated);
    next('preview', 'Perfecto. Revisa la previsualizacion y aplicala cuando estes listo.');
  };

  const applyTemplate = async () => {
    if (!agentId) return;
    try {
      setLoading(true);
      setError('');
      setOk('');
      const detailRes = await axios.get(endpoints.agentflow.agents.detail(tenantId, agentId));
      const detail = detailRes.data as AgentDetailDto;
      await axios.put(endpoints.agentflow.agents.update(tenantId, agentId), {
        name: detail.name,
        description: detail.description,
        tags: detail.tags ?? [],
        brain: detail.brain,
        loop: detail.loop,
        memory: detail.memory,
        session: detail.session,
        tools: detail.tools ?? [],
        steps: draftSteps,
      });
      setOk(`Sub-flujo aplicado correctamente a "${detail.name}".`);
      setMessages((prev) => [...prev, { role: 'assistant', content: 'Listo. Guarde el sub-flujo en el agente seleccionado.' }]);
      setStepState('apply');
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo aplicar el sub-flujo.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Stack spacing={2}>
      {error && <Alert severity="error">{error}</Alert>}
      {ok && <Alert severity="success">{ok}</Alert>}

      <ChatWizardHost
        title="Wizard de sub-flujo de agentes"
        subtitle="Configura un sub-flujo conversacional y aplicalo al agente."
        messages={messages}
        inputValue={input}
        inputPlaceholder="Escribe aqui para documentar contexto adicional..."
        onInputChange={setInput}
        onSend={onSend}
        loading={loading}
        sendDisabled={!input.trim()}
      >
        <Box sx={{ p: 1.25, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
          {stepState === 'agent' && (
            <Stack spacing={1.25}>
              <Typography variant="caption" color="text.secondary">Selecciona el agente objetivo</Typography>
              <Select size="small" fullWidth value={agentId} onChange={(e) => setAgentId(e.target.value)}>
                {agents.map((agent) => (
                  <MenuItem key={agent.id} value={agent.id}>{agent.name}</MenuItem>
                ))}
              </Select>
              <Button variant="contained" onClick={confirmAgent} disabled={!agentId}>Continuar</Button>
            </Stack>
          )}

          {stepState === 'template' && (
            <Stack spacing={1.25}>
              <Typography variant="caption" color="text.secondary">Selecciona una plantilla</Typography>
              <TextField select size="small" fullWidth label="Plantilla" value={templateId} onChange={(e) => setTemplateId(e.target.value as TemplateId)}>
                <MenuItem value="sales">Ventas</MenuItem>
                <MenuItem value="support">Soporte</MenuItem>
                <MenuItem value="collection">Cobros</MenuItem>
              </TextField>
              <Button variant="contained" onClick={confirmTemplate}>Generar subflujo</Button>
            </Stack>
          )}

          {(stepState === 'preview' || stepState === 'apply') && (
            <Stack spacing={1}>
              <Typography variant="caption" color="text.secondary">Previsualizacion de pasos</Typography>
              <Stack direction="row" spacing={0.75} flexWrap="wrap">
                {draftSteps.map((step) => (
                  <Chip key={step.id} label={`${step.label} (${step.type})`} />
                ))}
              </Stack>
              <Box sx={{ p: 1, borderRadius: 1, bgcolor: 'background.neutral' }}>
                {draftSteps.map((step) => (
                  <Typography key={step.id} variant="body2">
                    - {step.label}: {step.description}
                  </Typography>
                ))}
              </Box>
              {stepState === 'preview' && (
                <Stack direction="row" spacing={1}>
                  <Button variant="contained" onClick={applyTemplate} disabled={loading || draftSteps.length === 0}>Aplicar al agente</Button>
                  <Button variant="text" color="inherit" onClick={reset}>Reiniciar wizard</Button>
                </Stack>
              )}
            </Stack>
          )}
        </Box>
      </ChatWizardHost>
    </Stack>
  );
}

