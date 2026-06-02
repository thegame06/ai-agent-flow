import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import CardHeader from '@mui/material/CardHeader';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { useSettingsWorkspace } from 'src/aiagentflow/pages/settings/SettingsWorkspaceContext';

import { Iconify } from 'src/components/iconify';

type AgentContextSettings = {
  globalMarkdown: string;
  routerMarkdown: string;
  workflowMarkdown: string;
  configAssistantMarkdown: string;
  customMarkdown: string;
  whatsAppMarkdown: string;
  voiceMarkdown: string;
  callCenterMarkdown: string;
  webChatMarkdown: string;
  apiMarkdown: string;
  updatedAt?: string | null;
  updatedBy?: string | null;
};

const defaults: AgentContextSettings = {
  globalMarkdown: '',
  routerMarkdown: '',
  workflowMarkdown: '',
  configAssistantMarkdown: '',
  customMarkdown: '',
  whatsAppMarkdown: '',
  voiceMarkdown: '',
  callCenterMarkdown: '',
  webChatMarkdown: '',
  apiMarkdown: '',
  updatedAt: null,
  updatedBy: null,
};

const sections: Array<{ key: keyof AgentContextSettings; title: string; helper: string }> = [
  { key: 'globalMarkdown', title: 'Contexto global', helper: 'Se agrega a todos los agentes antes de ejecutar.' },
  { key: 'routerMarkdown', title: 'Router', helper: 'Reglas y contexto para clasificacion y enrutamiento.' },
  { key: 'workflowMarkdown', title: 'Workflow', helper: 'Contexto comun para agentes workflow.' },
  { key: 'configAssistantMarkdown', title: 'Config Assistant', helper: 'Guia para asistentes que crean o ajustan configuraciones.' },
  { key: 'customMarkdown', title: 'Agentes custom', helper: 'Contexto base para agentes creados por el tenant.' },
  { key: 'whatsAppMarkdown', title: 'WhatsApp', helper: 'Instrucciones especificas del canal WhatsApp.' },
  { key: 'voiceMarkdown', title: 'Voice', helper: 'Guiones o restricciones para experiencias de voz.' },
  { key: 'callCenterMarkdown', title: 'Call center', helper: 'Contexto especializado para llamadas de agentes u operadores.' },
  { key: 'webChatMarkdown', title: 'WebChat', helper: 'Contexto de canal web y asistentes incrustados.' },
  { key: 'apiMarkdown', title: 'API', helper: 'Contexto para mensajes o ejecuciones que entran por API.' },
];

export default function AgentContextSettingsPage() {
  const tenantId = useTenantId();
  const { embedded } = useSettingsWorkspace();
  const [settings, setSettings] = useState<AgentContextSettings>(defaults);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      setMessage(null);
      try {
        const res = await axios.get(`/api/v1/tenants/${tenantId}/settings/agent-contexts`);
        setSettings({ ...defaults, ...res.data });
      } catch (e: any) {
        setMessage(e?.message || 'No se pudieron cargar los contextos markdown.');
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [tenantId]);

  const save = async () => {
    setSaving(true);
    setMessage(null);
    try {
      const res = await axios.put(`/api/v1/tenants/${tenantId}/settings/agent-contexts`, settings);
      setSettings({ ...defaults, ...res.data });
      setMessage('Contextos markdown guardados correctamente.');
    } catch (e: any) {
      setMessage(e?.message || 'No se pudieron guardar los contextos markdown.');
    } finally {
      setSaving(false);
    }
  };

  const setValue = (key: keyof AgentContextSettings, value: string) =>
    setSettings((prev) => ({ ...prev, [key]: value }));

  return (
    <>
      <Helmet>
        <title>Contextos MD | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="lg" disablePadding={embedded}>
        <Box sx={{ mb: 4 }}>
          <Typography variant="h4">Contextos MD para agentes</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            Administra markdown inyectado en runtime por rol y por canal. Esto evita hardcodes en backend y te deja ajustar comportamiento sin recompilar.
          </Typography>
        </Box>

        {message && <Alert severity={message.includes('correctamente') ? 'success' : 'error'} sx={{ mb: 2 }}>{message}</Alert>}

        <Card sx={{ mb: 3 }}>
          <CardHeader
            title="Como se aplica"
            subheader="El runtime compone el system prompt con contexto global, luego por rol del agente y despues por canal si existe."
            avatar={<Iconify icon="mdi:file-document-edit-outline" width={28} />}
          />
          <Divider />
          <CardContent>
            <Stack spacing={1}>
              <Typography variant="body2">{'Orden actual: global -> rol -> canal.'}</Typography>
              <Typography variant="body2">Roles: `Router`, `WorkflowBrain`, `ConfigAssistant`, `Custom`.</Typography>
              <Typography variant="body2">Canales soportados en esta vista: `WhatsApp`, `Voice`, `CallCenter`, `WebChat`, `Api`.</Typography>
            </Stack>
          </CardContent>
        </Card>

        <Grid container spacing={3}>
          {sections.map((section) => (
            <Grid item xs={12} md={6} key={section.key}>
              <Card>
                <CardHeader title={section.title} subheader={section.helper} />
                <Divider />
                <CardContent>
                  <TextField
                    fullWidth
                    multiline
                    minRows={8}
                    label={`${section.title} markdown`}
                    value={(settings[section.key] as string) ?? ''}
                    onChange={(e) => setValue(section.key, e.target.value)}
                    disabled={loading}
                  />
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>

        <Box sx={{ mt: 3 }}>
          <Typography variant="caption" color="text.secondary">
            Ultima actualizacion: {settings.updatedAt ? new Date(settings.updatedAt).toLocaleString() : 'sin cambios guardados'} {settings.updatedBy ? `por ${settings.updatedBy}` : ''}
          </Typography>
        </Box>

        <Box sx={{ mt: 4, display: 'flex', justifyContent: 'flex-end' }}>
          <Button variant="contained" size="large" startIcon={<Iconify icon="mdi:content-save-outline" />} onClick={save} disabled={loading || saving}>
            {saving ? 'Guardando...' : 'Guardar contextos MD'}
          </Button>
        </Box>
      </DashboardContent>
    </>
  );
}
