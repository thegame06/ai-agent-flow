import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import FormControlLabel from '@mui/material/FormControlLabel';

import axios, { endpoints } from 'src/lib/axios';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { useWizardProgress } from './useWizardProgress';
import { ChatWizardHost, type ChatWizardMessage } from './ChatWizardHost';

type Channel = { id: string; name: string; type: string; status: string };
type IntentCatalogItem = { key: string; name: string; description: string; selected: boolean };
type StepId = 'goal' | 'channel' | 'data' | 'actions' | 'simulate';

type WizardState = {
  step: StepId;
  messages: ChatWizardMessage[];
  input: string;
  channelId: string;
  selectedIntentKeys: string[];
  dataFields: string[];
  actions: {
    createSale: boolean;
    createInvoice: boolean;
    escalate: boolean;
    confirm: boolean;
  };
  simulation: any;
};

type Props = { initialChannelId?: string };

const initialWizardState: WizardState = {
  step: 'goal',
  messages: [
    {
      role: 'assistant',
      content:
        'Vamos a crear tu automatizacion paso a paso. Que quieres lograr? Ejemplo: vender por WhatsApp y cobrar con factura.',
    },
  ],
  input: '',
  channelId: '',
  selectedIntentKeys: [],
  dataFields: ['Nombre', 'Producto', 'Cantidad'],
  actions: { createSale: true, createInvoice: false, escalate: false, confirm: true },
  simulation: null,
};

export function AutomationChatWizard({ initialChannelId }: Props) {
  const tenantId = useTenantId();
  const { state: saved, setState: setSaved, reset } = useWizardProgress<WizardState>(
    tenantId,
    'automation',
    initialWizardState
  );

  const [step, setStep] = useState<StepId>(saved.step);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [ok, setOk] = useState('');
  const [messages, setMessages] = useState<ChatWizardMessage[]>(saved.messages);
  const [input, setInput] = useState(saved.input);
  const [channels, setChannels] = useState<Channel[]>([]);
  const [channelId, setChannelId] = useState(saved.channelId);
  const [channelHealth, setChannelHealth] = useState<{ healthy: boolean; message?: string } | null>(null);
  const [intents, setIntents] = useState<IntentCatalogItem[]>([]);
  const [selectedIntentKeys, setSelectedIntentKeys] = useState<string[]>(saved.selectedIntentKeys);
  const [dataFields, setDataFields] = useState<string[]>(saved.dataFields);
  const [actions, setActions] = useState(saved.actions);
  const [simulation, setSimulation] = useState<any>(saved.simulation);

  const selectedChannel = useMemo(() => channels.find((c) => c.id === channelId) ?? null, [channelId, channels]);

  useEffect(() => {
    const loadChannels = async () => {
      setLoading(true);
      try {
        const res = await axios.get(endpoints.agentflow.channels.list(tenantId));
        const list = (res.data ?? []) as Channel[];
        setChannels(list);
        if (!channelId) {
          if (initialChannelId && list.some((x) => x.id === initialChannelId)) setChannelId(initialChannelId);
          else if (list.length > 0) setChannelId(list[0].id);
        }
      } catch (e: any) {
        setError(e?.message ?? 'No se pudieron cargar canales.');
      } finally {
        setLoading(false);
      }
    };
    loadChannels();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, initialChannelId]);

  useEffect(() => {
    const loadIntents = async () => {
      if (!channelId) return;
      try {
        const res = await axios.get(endpoints.agentflow.channels.intentsCatalog(tenantId, channelId));
        const items = (res.data?.items ?? []) as IntentCatalogItem[];
        setIntents(items);
        if (selectedIntentKeys.length === 0) {
          setSelectedIntentKeys(items.filter((x) => x.selected).map((x) => x.key));
        }
      } catch {
        setIntents([]);
      }
    };
    loadIntents();
  }, [channelId, selectedIntentKeys.length, tenantId]);

  useEffect(() => {
    setSaved({
      step,
      messages,
      input,
      channelId,
      selectedIntentKeys,
      dataFields,
      actions,
      simulation,
    });
  }, [actions, channelId, dataFields, input, messages, selectedIntentKeys, setSaved, simulation, step]);

  const goNext = (nextStep: StepId, assistantText: string) => {
    setStep(nextStep);
    setMessages((prev) => [...prev, { role: 'assistant', content: assistantText }]);
  };

  const onSend = async () => {
    const text = input.trim();
    if (!text || loading) return;
    setInput('');
    setMessages((prev) => [...prev, { role: 'user', content: text }]);

    if (step === 'goal') {
      goNext('channel', 'Perfecto. Ahora selecciona el canal y valida su health.');
      return;
    }
    if (step === 'channel') {
      goNext('data', 'Excelente. Define que datos debe recopilar del cliente.');
      return;
    }
    if (step === 'data') {
      setDataFields((prev) => (prev.includes(text) ? prev : [...prev, text]));
      return;
    }
    if (step === 'actions') {
      goNext('simulate', 'Ultimo paso: simula un mensaje real y guarda el borrador.');
      return;
    }

    if (step === 'simulate') {
      try {
        setLoading(true);
        const res = await axios.post(endpoints.agentflow.intentRouting.classify(tenantId), {
          message: text,
          channel: selectedChannel?.type?.toLowerCase() ?? '',
        });
        setSimulation(res.data);
        setMessages((prev) => [
          ...prev,
          {
            role: 'assistant',
            content: `Clasificacion: ${res.data?.best_match?.intent_key ?? 'sin match'} (${res.data?.confidence ?? 'N/A'})`,
          },
        ]);
      } catch (e: any) {
        setError(e?.message ?? 'No se pudo simular el mensaje.');
      } finally {
        setLoading(false);
      }
    }
  };

  const checkHealth = async () => {
    if (!channelId) return;
    try {
      const res = await axios.get(endpoints.agentflow.channels.status(tenantId, channelId));
      setChannelHealth({ healthy: Boolean(res.data?.healthy), message: res.data?.message });
    } catch (e: any) {
      setChannelHealth({ healthy: false, message: e?.message ?? 'Health check fallido.' });
    }
  };

  const applyDraft = async () => {
    if (!channelId) return;
    try {
      setLoading(true);
      setError('');
      await axios.post(endpoints.agentflow.channels.intentsApply(tenantId, channelId), {
        intentKeys: selectedIntentKeys,
      });
      setOk('Borrador aplicado. Las intenciones ya quedaron cargadas en el canal.');
      setMessages((prev) => [...prev, { role: 'assistant', content: 'Listo. Guarde el borrador de automatizacion.' }]);
    } catch (e: any) {
      setError(e?.message ?? 'No se pudo aplicar el borrador.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Stack spacing={2}>
      {error && <Alert severity="error">{error}</Alert>}
      {ok && <Alert severity="success">{ok}</Alert>}

      <ChatWizardHost
        title="Asistente de automatizaciones"
        subtitle="Experiencia guiada en chat para configurar flujos sin complejidad tecnica."
        messages={messages}
        inputValue={input}
        inputPlaceholder={step === 'simulate' ? 'Escribe un mensaje de cliente...' : 'Escribe y presiona Enter...'}
        onInputChange={setInput}
        onSend={onSend}
        loading={loading}
        sendDisabled={!input.trim()}
      >
        <Box sx={{ p: 1.25, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
          {step === 'goal' && (
            <Stack direction="row" spacing={1} flexWrap="wrap">
              {['Vender', 'Cobrar', 'Soporte', 'Agendar', 'Seguimiento'].map((goal) => (
                <Chip key={goal} label={goal} onClick={() => setInput(goal)} color="primary" variant="outlined" />
              ))}
            </Stack>
          )}

          {step === 'channel' && (
            <Stack spacing={1.25}>
              <TextField select size="small" label="Canal" value={channelId} onChange={(e) => setChannelId(e.target.value)} fullWidth>
                {channels.map((channel) => (
                  <MenuItem key={channel.id} value={channel.id}>
                    {channel.name} ({channel.type}) - {channel.status}
                  </MenuItem>
                ))}
              </TextField>
              <Stack direction="row" spacing={1}>
                <Button size="small" variant="outlined" onClick={checkHealth}>Validar health</Button>
                <Button size="small" variant="contained" onClick={() => goNext('data', 'Perfecto. Ahora define los datos a recopilar.')}>
                  Continuar
                </Button>
              </Stack>
              {channelHealth && (
                <Typography variant="caption" color={channelHealth.healthy ? 'success.main' : 'warning.main'}>
                  {channelHealth.healthy ? 'Canal saludable' : 'Canal con observaciones'} {channelHealth.message ?? ''}
                </Typography>
              )}
            </Stack>
          )}

          {step === 'data' && (
            <Stack spacing={1}>
              <Typography variant="caption" color="text.secondary">Escribe un campo en el chat para agregarlo. Campos actuales:</Typography>
              <Stack direction="row" spacing={0.75} flexWrap="wrap">
                {dataFields.map((field) => (
                  <Chip key={field} label={field} onDelete={() => setDataFields((prev) => prev.filter((x) => x !== field))} />
                ))}
              </Stack>
              <Button size="small" variant="contained" onClick={() => goNext('actions', 'Ahora define las acciones finales.')}>
                Continuar
              </Button>
            </Stack>
          )}

          {step === 'actions' && (
            <Stack spacing={0.25}>
              <FormControlLabel control={<Checkbox checked={actions.createSale} onChange={(e) => setActions((p) => ({ ...p, createSale: e.target.checked }))} />} label="Crear venta" />
              <FormControlLabel control={<Checkbox checked={actions.createInvoice} onChange={(e) => setActions((p) => ({ ...p, createInvoice: e.target.checked }))} />} label="Generar factura" />
              <FormControlLabel control={<Checkbox checked={actions.escalate} onChange={(e) => setActions((p) => ({ ...p, escalate: e.target.checked }))} />} label="Escalar a humano" />
              <FormControlLabel control={<Checkbox checked={actions.confirm} onChange={(e) => setActions((p) => ({ ...p, confirm: e.target.checked }))} />} label="Enviar confirmacion" />
              <Button size="small" variant="contained" onClick={() => goNext('simulate', 'Simula un mensaje y guarda el borrador.')}>
                Continuar
              </Button>
            </Stack>
          )}

          {step === 'simulate' && (
            <Stack spacing={1}>
              <Typography variant="caption" color="text.secondary">Intenciones a cargar para este canal:</Typography>
              <Box sx={{ maxHeight: 180, overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1, p: 0.75 }}>
                <Stack spacing={0.25}>
                  {intents.map((intent) => (
                    <FormControlLabel
                      key={intent.key}
                      control={
                        <Checkbox
                          checked={selectedIntentKeys.includes(intent.key)}
                          onChange={(e) => {
                            const checked = e.target.checked;
                            setSelectedIntentKeys((prev) => (checked ? [...prev, intent.key] : prev.filter((x) => x !== intent.key)));
                          }}
                        />
                      }
                      label={`${intent.name} (${intent.key})`}
                    />
                  ))}
                </Stack>
              </Box>
              {simulation && (
                <Alert severity="info">
                  Mejor match: {simulation?.best_match?.intent_key ?? 'Sin match'} | Confianza: {simulation?.confidence ?? 'N/A'}
                </Alert>
              )}
              <Stack direction="row" spacing={1}>
                <Button variant="contained" onClick={applyDraft} disabled={loading || !channelId}>Guardar borrador</Button>
                <Button variant="text" color="inherit" onClick={reset}>Reiniciar wizard</Button>
              </Stack>
            </Stack>
          )}
        </Box>
      </ChatWizardHost>
    </Stack>
  );
}
