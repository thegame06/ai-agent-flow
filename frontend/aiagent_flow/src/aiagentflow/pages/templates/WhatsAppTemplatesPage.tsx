import { useNavigate } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';

import axios from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { TemplateDrawer } from './TemplateDrawer';
import { TemplateEditorDialog } from './TemplateEditorDialog';
import { WhatsAppMetaDialog } from '../channels/WhatsAppMetaDialog';
import { slots, QUALITY, isTwilio, statusOf, providerLabel, CATEGORY_LABEL, templateFitsChannel } from './templateModel';

import type { WaTemplate, ChannelMeta, TemplateField } from './templateModel';

// ----------------------------------------------------------------------

type ListResponse = { items: WaTemplate[]; channels: ChannelMeta[]; fields: TemplateField[] };
type SyncResult = { accounts: number; created: number; updated: number; statusChanged: number; unchanged: number; removed: number; errors: string[] };

/**
 * Templates live in Meta accounts (WABA) or Twilio accounts; numbers are WhatsApp channels configured
 * in Channels. A company can have one account per area (collections, sales, marketing), or several
 * numbers in one.
 */
export default function WhatsAppTemplatesPage() {
  const tenantId = useTenantId();
  const navigate = useNavigate();
  const base = `/api/v1/tenants/${tenantId}/whatsapp/templates`;

  const [data, setData] = useState<ListResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [channelId, setChannelId] = useState('');
  const [status, setStatus] = useState('');
  const [query, setQuery] = useState('');
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState<SyncResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<{ open: boolean; template: WaTemplate | null }>({ open: false, template: null });
  const [openId, setOpenId] = useState<string | null>(null);
  const [metaChannelId, setMetaChannelId] = useState<string | null>(null);

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

  const channels = data?.channels ?? [];
  const fields = data?.fields ?? [];
  const selected = channels.find((c) => c.channelId === channelId) ?? null;
  const ready = channels.filter((c) => isTwilio(c) || (c.hasToken && c.businessAccountId));

  // Channels grouped by account (Meta or Twilio), to show which numbers share templates.
  const accounts = Array.from(
    channels.reduce((map, c) => {
      const key = isTwilio(c) ? `twilio-${c.connectionId ?? ''}` : c.businessAccountId || `sin-cuenta-${c.channelId}`;
      map.set(key, [...(map.get(key) ?? []), c]);
      return map;
    }, new Map<string, ChannelMeta[]>())
  );

  const items = (data?.items ?? []).filter(
    (t) =>
      (!selected || templateFitsChannel(t, selected)) &&
      (!status || t.status === status) &&
      (!query || t.name.includes(query.toLowerCase()))
  );

  const sync = async () => {
    setSyncing(true);
    setError(null);
    setSyncResult(null);
    try {
      const res = await axios.post<SyncResult>(`${base}/sync`, null, { params: { channelId: channelId || undefined } });
      setSyncResult(res.data);
      load();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudieron sincronizar las plantillas.');
    } finally {
      setSyncing(false);
    }
  };

  const channelNames = (t: WaTemplate) => channels.filter((c) => templateFitsChannel(t, c)).map((c) => c.channelName);

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
              Mensajes aprobados por WhatsApp. Son obligatorios para escribirle primero a un cliente o pasadas 24 h de su
              último mensaje. Cada cuenta (Meta o Twilio) tiene sus plantillas; los números se configuran en Canales.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button variant="outlined" startIcon={<Iconify icon="mdi:sync" />} disabled={!ready.length || syncing} onClick={sync}>
              {syncing ? 'Sincronizando…' : selected ? `Sincronizar ${selected.channelName}` : 'Sincronizar'}
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mdi:plus" />} disabled={!ready.length} onClick={() => setEditor({ open: true, template: null })}>
              Nueva plantilla
            </Button>
          </Stack>
        </Stack>

        {!loading && channels.length === 0 && (
          <Alert
            severity="info"
            sx={{ mb: 3 }}
            action={
              <Button color="inherit" size="small" onClick={() => navigate(paths.dashboard.system.channels)}>
                Ir a Canales
              </Button>
            }
          >
            Crea un canal de WhatsApp con la API de WhatsApp Business (Meta) o con Twilio en Canales. Cada número es un canal
            (por ejemplo Cobranza, Ventas y Marketing) y puede tener su propia cuenta.
          </Alert>
        )}

        {accounts.length > 0 && (
          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: 'repeat(auto-fill, minmax(320px, 1fr))' }, mb: 3 }}>
            {accounts.map(([key, group]) => (
              <Card key={key} variant="outlined" sx={{ p: 2 }}>
                <Typography variant="overline" color="text.secondary">
                  {isTwilio(group[0]) ? 'Cuenta de Twilio' : 'Cuenta de Meta'}
                </Typography>
                <Typography variant="subtitle2">
                  {isTwilio(group[0])
                    ? `Conexión ${group[0].connectionId || 'predeterminada'}`
                    : group[0].businessAccountId
                      ? `WABA ${group[0].businessAccountId}`
                      : 'Sin cuenta configurada'}
                </Typography>
                <Stack spacing={0.75} sx={{ mt: 1 }}>
                  {group.map((c) => (
                    <Stack key={c.channelId} direction="row" spacing={1} alignItems="center" justifyContent="space-between">
                      <Stack direction="row" spacing={1} alignItems="center">
                        <Iconify icon="mdi:whatsapp" />
                        <Typography variant="body2">{c.channelName}</Typography>
                        {!isTwilio(c) && !c.hasToken && <Chip size="small" color="error" label="Sin token" />}
                        {!isTwilio(c) && c.hasToken && !c.hasAppSecret && <Chip size="small" color="warning" label="Sin app secret" />}
                        {c.tokenInPlainConfig && <Chip size="small" color="warning" label="Token sin cifrar" />}
                      </Stack>
                      {!isTwilio(c) && (
                        <Button size="small" onClick={() => setMetaChannelId(c.channelId)}>
                          Cuenta de Meta
                        </Button>
                      )}
                    </Stack>
                  ))}
                </Stack>
              </Card>
            ))}
          </Box>
        )}

        {syncResult && (
          <Alert severity={syncResult.errors.length ? 'warning' : 'success'} sx={{ mb: 2 }} onClose={() => setSyncResult(null)}>
            {syncResult.errors.length
              ? syncResult.errors.join(' · ')
              : `Sincronizado (${syncResult.accounts} cuenta${syncResult.accounts === 1 ? '' : 's'}): ${syncResult.created} nuevas, ${syncResult.updated} con cambios (nueva versión), ${syncResult.statusChanged} cambiaron de estado, ${syncResult.removed} eliminadas, ${syncResult.unchanged} sin cambios.`}
          </Alert>
        )}
        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        <Card>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2 }}>
            <TextField select size="small" label="Canal" value={channelId} onChange={(e) => setChannelId(e.target.value)} sx={{ minWidth: 220 }}>
              <MenuItem value="">Todos los canales</MenuItem>
              {channels.map((c) => (
                <MenuItem key={c.channelId} value={c.channelId}>
                  {c.channelName}
                </MenuItem>
              ))}
            </TextField>
            <TextField size="small" placeholder="Buscar por nombre" value={query} onChange={(e) => setQuery(e.target.value)} sx={{ minWidth: 220 }} />
            <TextField select size="small" label="Estado" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ minWidth: 180 }}>
              <MenuItem value="">Todos</MenuItem>
              {['APPROVED', 'PENDING', 'UNSUBMITTED', 'REJECTED', 'PAUSED', 'DISABLED', 'DELETED'].map((s) => (
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
                  <TableCell>Canales</TableCell>
                  <TableCell>Idioma</TableCell>
                  <TableCell>Categoría</TableCell>
                  <TableCell>Estado</TableCell>
                  <TableCell>Calidad</TableCell>
                  <TableCell align="right">Variables</TableCell>
                  <TableCell align="right">Versión</TableCell>
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
                      {ready.length
                        ? 'No hay plantillas. Sincroniza para traer las que ya tienes en Meta o Twilio, o crea una nueva.'
                        : 'Configura un canal de WhatsApp (Meta o Twilio) para empezar.'}
                    </TableCell>
                  </TableRow>
                )}
                {!loading &&
                  items.map((t) => {
                    const st = statusOf(t.status);
                    const total = slots(t.components).length;
                    const mapped = t.parameters.filter((p) => (p.source === 'field' ? !!p.field : !!p.staticValue)).length;
                    const names = channelNames(t);
                    return (
                      <TableRow key={t.id} hover sx={{ cursor: 'pointer', opacity: t.status === 'DELETED' ? 0.5 : 1 }} onClick={() => setOpenId(t.id)}>
                        <TableCell>
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Typography variant="subtitle2">{t.name}</Typography>
                            <Chip size="small" variant="outlined" label={providerLabel(t)} />
                          </Stack>
                          {(t.status === 'REJECTED' || t.status === 'UNSUBMITTED') && t.rejectedReason && (
                            <Typography variant="caption" color="error.main">
                              {t.rejectedReason}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>{names.length ? names.join(', ') : <Typography variant="caption" color="text.secondary">Sin canal</Typography>}</TableCell>
                        <TableCell>{t.language}</TableCell>
                        <TableCell>{CATEGORY_LABEL[t.category] ?? t.category}</TableCell>
                        <TableCell>
                          <Chip size="small" label={st.label} color={st.color} variant="soft" />
                        </TableCell>
                        <TableCell>
                          {t.qualityScore ? (
                            <Chip size="small" variant="outlined" label={QUALITY[t.qualityScore]?.label ?? t.qualityScore} color={QUALITY[t.qualityScore]?.color ?? 'default'} />
                          ) : (
                            '—'
                          )}
                        </TableCell>
                        <TableCell align="right">
                          {total === 0 ? '—' : <Chip size="small" variant="soft" color={mapped === total ? 'success' : 'warning'} label={`${mapped}/${total} mapeadas`} />}
                        </TableCell>
                        <TableCell align="right">v{t.version}</TableCell>
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
        channels={ready}
        defaultChannelId={selected?.channelId}
        fields={fields}
        onClose={() => setEditor({ open: false, template: null })}
        onSaved={() => {
          setEditor({ open: false, template: null });
          setOpenId(null);
          load();
        }}
      />
      <WhatsAppMetaDialog tenantId={tenantId} channelId={metaChannelId} onClose={() => setMetaChannelId(null)} onSaved={() => load()} />
    </>
  );
}
