import { useState, useEffect } from 'react';

import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';

// ----------------------------------------------------------------------

/** A WhatsApp channel's Meta account as the API returns it (never the secrets). */
export type ChannelMeta = {
  channelId: string;
  channelName: string;
  status: string;
  connectionId?: string | null;
  businessAccountId?: string | null;
  phoneNumberId?: string | null;
  appId?: string | null;
  hasToken: boolean;
  hasAppSecret: boolean;
  tokenInPlainConfig: boolean;
};

export type MetaAccountForm = { businessAccountId: string; phoneNumberId: string; appId: string; accessToken: string; appSecret: string };

export const emptyMetaForm = (): MetaAccountForm => ({ businessAccountId: '', phoneNumberId: '', appId: '', accessToken: '', appSecret: '' });

/** Saves a channel's Meta account (the secret is encrypted; empty token/secret keep the stored ones). */
export const saveChannelMeta = (tenantId: string, channelId: string, form: MetaAccountForm) =>
  axios.put<ChannelMeta>(`/api/v1/tenants/${tenantId}/channels/${channelId}/meta`, {
    businessAccountId: form.businessAccountId.trim(),
    phoneNumberId: form.phoneNumberId.trim(),
    appId: form.appId.trim() || null,
    accessToken: form.accessToken.trim() || null,
    appSecret: form.appSecret.trim() || null,
  });

/** The fields of a Meta (WhatsApp Business) account; used when creating a channel and in the account dialog. */
export function MetaAccountFields({
  form,
  onChange,
  stored,
}: {
  form: MetaAccountForm;
  onChange: (form: MetaAccountForm) => void;
  stored?: { hasToken: boolean; hasAppSecret: boolean } | null;
}) {
  const set = (key: keyof MetaAccountForm, value: string) => onChange({ ...form, [key]: value });
  return (
    <Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">
        En Meta for Developers, en tu app → WhatsApp → Configuración de la API, están el id de la cuenta de WhatsApp Business
        (WABA) y el id del número. Usa un token de un usuario del sistema con permisos whatsapp_business_management y
        whatsapp_business_messaging. El app secret está en Configuración → Básica.
      </Typography>
      <TextField label="Id de la cuenta de WhatsApp Business (WABA)" value={form.businessAccountId} onChange={(e) => set('businessAccountId', e.target.value)} />
      <TextField label="Id del número de teléfono" value={form.phoneNumberId} onChange={(e) => set('phoneNumberId', e.target.value)} />
      <TextField label="Id de la app (opcional)" value={form.appId} onChange={(e) => set('appId', e.target.value)} />
      <TextField
        label="Token de acceso"
        type="password"
        autoComplete="off"
        value={form.accessToken}
        onChange={(e) => set('accessToken', e.target.value)}
        helperText={stored?.hasToken ? 'Guardado. Déjalo vacío para conservarlo.' : 'Se guarda cifrado y no se vuelve a mostrar.'}
      />
      <TextField
        label="App secret"
        type="password"
        autoComplete="off"
        value={form.appSecret}
        onChange={(e) => set('appSecret', e.target.value)}
        helperText={stored?.hasAppSecret ? 'Guardado. Déjalo vacío para conservarlo.' : 'Verifica que los webhooks vienen de Meta. Se guarda cifrado.'}
      />
    </Stack>
  );
}

type Props = {
  tenantId: string;
  channelId: string | null;
  onClose: () => void;
  onSaved?: (meta: ChannelMeta) => void;
};

/**
 * The Meta account of one WhatsApp channel. Each number (collections, sales, marketing) is its own
 * channel; several can share a Meta account. Used from Channels and from the templates page.
 */
export function WhatsAppMetaDialog({ tenantId, channelId, onClose, onSaved }: Props) {
  const [meta, setMeta] = useState<ChannelMeta | null>(null);
  const [form, setForm] = useState<MetaAccountForm>(emptyMetaForm());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!channelId) return;
    setMeta(null);
    setError(null);
    axios
      .get<ChannelMeta>(`/api/v1/tenants/${tenantId}/channels/${channelId}/meta`)
      .then((res) => {
        setMeta(res.data);
        setForm({
          businessAccountId: res.data.businessAccountId ?? '',
          phoneNumberId: res.data.phoneNumberId ?? '',
          appId: res.data.appId ?? '',
          accessToken: '',
          appSecret: '',
        });
      })
      .catch(() => setError('No se pudo cargar el canal.'));
  }, [tenantId, channelId]);

  const save = async () => {
    if (!channelId) return;
    setBusy(true);
    setError(null);
    try {
      const res = await saveChannelMeta(tenantId, channelId, form);
      onSaved?.(res.data);
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo guardar la cuenta de Meta.');
    } finally {
      setBusy(false);
    }
  };

  const canSave = form.businessAccountId.trim() && form.phoneNumberId.trim() && (meta?.hasToken || meta?.tokenInPlainConfig || form.accessToken.trim());

  return (
    <Dialog open={!!channelId} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Cuenta de Meta{meta ? ` · ${meta.channelName}` : ''}</DialogTitle>
      <DialogContent>
        {!meta && !error && <LinearProgress />}
        {meta && (
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              <Chip size="small" color={meta.hasToken ? 'success' : 'default'} label={meta.hasToken ? 'Token guardado' : 'Sin token'} />
              <Chip size="small" color={meta.hasAppSecret ? 'success' : 'warning'} label={meta.hasAppSecret ? 'App secret guardado' : 'Sin app secret'} />
            </Stack>
            {meta.tokenInPlainConfig && (
              <Alert severity="warning">
                Este canal guarda el token en su configuración sin cifrar. Al guardar se mueve a una conexión cifrada.
              </Alert>
            )}
            <MetaAccountFields form={form} onChange={setForm} stored={meta} />
          </Stack>
        )}
        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={busy || !canSave} onClick={save}>
          Guardar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
