import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { TemplateDrawer } from './TemplateDrawer';
import { TemplateEditorDialog } from './TemplateEditorDialog';
import { slots, QUALITY, statusOf, CATEGORY_LABEL } from './templateModel';

import type { WaTemplate, MetaAccount, TemplateField } from './templateModel';

// ----------------------------------------------------------------------

type ListResponse = { items: WaTemplate[]; accounts: MetaAccount[]; fields: TemplateField[] };
type SyncResult = { accounts: number; created: number; updated: number; statusChanged: number; unchanged: number; removed: number; errors: string[] };

export default function WhatsAppTemplatesPage() {
  const tenantId = useTenantId();
  const base = `/api/v1/tenants/${tenantId}/whatsapp/templates`;

  const [data, setData] = useState<ListResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState('');
  const [query, setQuery] = useState('');
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState<SyncResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<{ open: boolean; template: WaTemplate | null }>({ open: false, template: null });
  const [openId, setOpenId] = useState<string | null>(null);
  const [connectOpen, setConnectOpen] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    axios
      .get<ListResponse>(base)
      .then((res) => setData(res.data))
      .catch(() => setError('No se pudieron cargar las plantillas.'))
      .finally(() => setLoading(false));
  }, [base]);

  useEffect(() => {
    load();
  }, [load]);

  const sync = async () => {
    setSyncing(true);
    setError(null);
    setSyncResult(null);
    try {
      const res = await axios.post<SyncResult>(`${base}/sync`);
      setSyncResult(res.data);
      load();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo sincronizar con Meta.');
    } finally {
      setSyncing(false);
    }
  };

  const accounts = data?.accounts ?? [];
  const fields = data?.fields ?? [];
  const items = (data?.items ?? []).filter(
    (t) => (!status || t.status === status) && (!query || t.name.includes(query.toLowerCase()))
  );

  return (
    <>
      <Helmet>
        <title>Plantillas de WhatsApp</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} sx={{ mb: 3 }}>
          <Box>
            <Typography variant="h4">Plantillas de WhatsApp</Typography>
            <Typography variant="body2" color="text.secondary">
              Mensajes aprobados por Meta. Son obligatorios para escribirle primero a un cliente o pasadas 24 h de su último
              mensaje.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button variant="outlined" startIcon={<Iconify icon="mdi:link-variant" />} onClick={() => setConnectOpen(true)}>
              {accounts.length ? 'Cuenta de Meta' : 'Conectar cuenta de Meta'}
            </Button>
            <Button variant="outlined" startIcon={<Iconify icon="mdi:sync" />} disabled={!accounts.length || syncing} onClick={sync}>
              {syncing ? 'Sincronizando…' : 'Sincronizar con Meta'}
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mdi:plus" />} disabled={!accounts.length} onClick={() => setEditor({ open: true, template: null })}>
              Nueva plantilla
            </Button>
          </Stack>
        </Stack>

        {!loading && accounts.length === 0 && (
          <Alert severity="info" sx={{ mb: 3 }}>
            Conecta la cuenta de WhatsApp Business de Meta para traer sus plantillas, crear nuevas y usarlas en campañas.
            Necesitas el id de la cuenta de WhatsApp Business, el id del número y un token de acceso de Meta for Developers.
          </Alert>
        )}
        {accounts.map((a) => (
          <Stack key={a.connectionId} direction="row" spacing={1} sx={{ mb: 2 }} flexWrap="wrap" useFlexGap>
            <Chip icon={<Iconify icon="mdi:whatsapp" />} label={a.name} variant="outlined" />
            <Chip size="small" label={`Cuenta ${a.businessAccountId ?? 'sin id'}`} />
            {!a.hasToken && <Chip size="small" color="error" label="Sin token" />}
            {!a.hasAppSecret && <Chip size="small" color="warning" label="Sin app secret: el webhook no se puede verificar" />}
          </Stack>
        ))}

        {syncResult && (
          <Alert severity={syncResult.errors.length ? 'warning' : 'success'} sx={{ mb: 2 }} onClose={() => setSyncResult(null)}>
            {syncResult.errors.length
              ? syncResult.errors.join(' · ')
              : `Sincronizado: ${syncResult.created} nuevas, ${syncResult.updated} con cambios (nueva versión), ${syncResult.statusChanged} cambiaron de estado, ${syncResult.removed} eliminadas en Meta, ${syncResult.unchanged} sin cambios.`}
          </Alert>
        )}
        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        <Card>
          <Stack direction="row" spacing={2} sx={{ p: 2 }}>
            <TextField size="small" placeholder="Buscar por nombre" value={query} onChange={(e) => setQuery(e.target.value)} sx={{ minWidth: 240 }} />
            <TextField select size="small" label="Estado" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ minWidth: 180 }}>
              <MenuItem value="">Todos</MenuItem>
              {['APPROVED', 'PENDING', 'REJECTED', 'PAUSED', 'DISABLED', 'DELETED'].map((s) => (
                <MenuItem key={s} value={s}>
                  {statusOf(s).label}
                </MenuItem>
              ))}
            </TextField>
          </Stack>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Nombre</TableCell>
                  <TableCell>Idioma</TableCell>
                  <TableCell>Categoría</TableCell>
                  <TableCell>Estado</TableCell>
                  <TableCell>Calidad</TableCell>
                  <TableCell align="right">Variables</TableCell>
                  <TableCell align="right">Versión</TableCell>
                  <TableCell>Origen</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {loading && (
                  <TableRow>
                    <TableCell colSpan={8} align="center" sx={{ py: 4 }}>
                      <CircularProgress size={24} />
                    </TableCell>
                  </TableRow>
                )}
                {!loading && items.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={8} align="center" sx={{ py: 4, color: 'text.secondary' }}>
                      {accounts.length
                        ? 'No hay plantillas. Sincroniza con Meta para traer las que ya tienes, o crea una nueva.'
                        : 'Conecta tu cuenta de Meta para empezar.'}
                    </TableCell>
                  </TableRow>
                )}
                {!loading &&
                  items.map((t) => {
                    const st = statusOf(t.status);
                    const total = slots(t.components).length;
                    const mapped = t.parameters.filter((p) => (p.source === 'field' ? !!p.field : !!p.staticValue)).length;
                    return (
                      <TableRow key={t.id} hover sx={{ cursor: 'pointer', opacity: t.status === 'DELETED' ? 0.5 : 1 }} onClick={() => setOpenId(t.id)}>
                        <TableCell>
                          <Typography variant="subtitle2">{t.name}</Typography>
                          {t.status === 'REJECTED' && t.rejectedReason && (
                            <Typography variant="caption" color="error.main">
                              {t.rejectedReason}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>{t.language}</TableCell>
                        <TableCell>{CATEGORY_LABEL[t.category] ?? t.category}</TableCell>
                        <TableCell>
                          <Chip size="small" label={st.label} color={st.color} variant="soft" />
                        </TableCell>
                        <TableCell>
                          {t.qualityScore ? <Chip size="small" variant="outlined" label={QUALITY[t.qualityScore]?.label ?? t.qualityScore} color={QUALITY[t.qualityScore]?.color ?? 'default'} /> : '—'}
                        </TableCell>
                        <TableCell align="right">
                          {total === 0 ? '—' : (
                            <Chip size="small" variant="soft" color={mapped === total ? 'success' : 'warning'} label={`${mapped}/${total} mapeadas`} />
                          )}
                        </TableCell>
                        <TableCell align="right">v{t.version}</TableCell>
                        <TableCell>{t.origin === 'agentflow' ? 'Creada aquí' : 'Meta'}</TableCell>
                      </TableRow>
                    );
                  })}
              </TableBody>
            </Table>
          </TableContainer>
        </Card>
      </DashboardContent>

      <TemplateDrawer
        base={base}
        templateId={openId}
        fields={fields}
        onClose={() => setOpenId(null)}
        onEdit={(t) => setEditor({ open: true, template: t })}
        onChanged={load}
      />
      <TemplateEditorDialog
        base={base}
        open={editor.open}
        template={editor.template}
        accounts={accounts}
        fields={fields}
        onClose={() => setEditor({ open: false, template: null })}
        onSaved={() => {
          setEditor({ open: false, template: null });
          setOpenId(null);
          load();
        }}
      />
      <ConnectMetaDialog
        tenantId={tenantId}
        open={connectOpen}
        account={accounts[0] ?? null}
        onClose={() => setConnectOpen(false)}
        onSaved={() => {
          setConnectOpen(false);
          load();
        }}
      />
    </>
  );
}

// ----------------------------------------------------------------------

/**
 * Saves the Meta account as a tenant connection: ids in the config, token and app secret in the
 * encrypted secret (never shown again).
 */
function ConnectMetaDialog({
  tenantId,
  open,
  account,
  onClose,
  onSaved,
}: {
  tenantId: string;
  open: boolean;
  account: MetaAccount | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({ name: '', businessAccountId: '', phoneNumberId: '', appId: '', accessToken: '', appSecret: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open)
      setForm({
        name: account?.name ?? 'WhatsApp Business',
        businessAccountId: account?.businessAccountId ?? '',
        phoneNumberId: account?.phoneNumberId ?? '',
        appId: '',
        accessToken: '',
        appSecret: '',
      });
    setError(null);
  }, [open, account]);

  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }));

  const save = async () => {
    // The secret is replaced as a whole: changing one value alone would erase the other.
    if (account && !!form.accessToken.trim() !== !!form.appSecret.trim()) {
      setError('Para cambiar las credenciales escribe el token y el app secret juntos.');
      return;
    }
    setBusy(true);
    setError(null);
    const id = account?.connectionId ?? 'meta-whatsapp';
    try {
      await axios.put(`/api/v1/tenants/${tenantId}/connections/${id}`, {
        name: form.name,
        type: 'Messaging',
        connectorId: 'whatsapp-business',
        config: {
          provider: 'meta',
          businessAccountId: form.businessAccountId.trim(),
          phoneNumberId: form.phoneNumberId.trim(),
          ...(form.appId.trim() ? { appId: form.appId.trim() } : {}),
        },
        allowedAgentIds: [],
        allowedNodeIds: [],
        allowedConnectorIds: [],
      });
      // Only replace the secret when new values were typed (they are never read back).
      if (form.accessToken.trim() || form.appSecret.trim() || !account) {
        await axios.post(`/api/v1/tenants/${tenantId}/connections/${id}/secret`, {
          secret: JSON.stringify({ apiToken: form.accessToken.trim(), appSecret: form.appSecret.trim() }),
          expiresAt: null,
        });
      }
      onSaved();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo guardar la conexión.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Cuenta de WhatsApp Business (Meta)</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            En Meta for Developers, dentro de tu app, en WhatsApp → Configuración de la API, están el id de la cuenta de
            WhatsApp Business y el id del número. El token debe ser de un usuario del sistema con permisos
            whatsapp_business_management y whatsapp_business_messaging. El app secret está en Configuración → Básica.
          </Typography>
          <TextField label="Nombre" value={form.name} onChange={(e) => set('name', e.target.value)} />
          <TextField label="Id de la cuenta de WhatsApp Business (WABA)" value={form.businessAccountId} onChange={(e) => set('businessAccountId', e.target.value)} />
          <TextField label="Id del número de teléfono" value={form.phoneNumberId} onChange={(e) => set('phoneNumberId', e.target.value)} />
          <TextField label="Id de la app (opcional)" value={form.appId} onChange={(e) => set('appId', e.target.value)} />
          <TextField
            label="Token de acceso"
            type="password"
            value={form.accessToken}
            onChange={(e) => set('accessToken', e.target.value)}
            helperText={account ? 'Déjalo vacío para conservar el actual.' : 'Se guarda cifrado y no se vuelve a mostrar.'}
            autoComplete="off"
          />
          <TextField
            label="App secret"
            type="password"
            value={form.appSecret}
            onChange={(e) => set('appSecret', e.target.value)}
            helperText="Verifica que los webhooks vienen de Meta. Se guarda cifrado."
            autoComplete="off"
          />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={busy || !form.businessAccountId.trim() || !form.phoneNumberId.trim() || (!account && !form.accessToken.trim())} onClick={save}>
          Guardar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
