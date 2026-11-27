import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LinearProgress from '@mui/material/LinearProgress';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { TemplateBubble } from './TemplateBubble';
import { slots, QUALITY, statusOf, CATEGORY_LABEL } from './templateModel';

import type { WaTemplate, TemplateField, TemplateParameter, TemplateComponent } from './templateModel';

// ----------------------------------------------------------------------

type Version = { id: string; version: number; status: string; source: string; createdAt: string; createdBy: string; components: TemplateComponent[] };

type Detail = { template: WaTemplate; versions: Version[]; preview: string; previewMissing: string[] };

type Props = {
  base: string;
  templateId: string | null;
  fields: TemplateField[];
  onClose: () => void;
  onEdit: (template: WaTemplate) => void;
  onChanged: () => void;
};

const SOURCE_LABEL: Record<string, string> = { sync: 'Sincronización', create: 'Creada', edit: 'Editada', webhook: 'Meta', delete: 'Eliminada' };

const componentLabel = (c: string) =>
  c === 'header' ? 'Encabezado' : c === 'body' ? 'Mensaje' : c === 'header_media' ? 'Archivo del encabezado' : `Botón ${Number(c.split(':')[1]) + 1}`;

export function TemplateDrawer({ base, templateId, fields, onClose, onEdit, onChanged }: Props) {
  const [detail, setDetail] = useState<Detail | null>(null);
  const [params, setParams] = useState<TemplateParameter[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!templateId) return;
    axios
      .get<Detail>(`${base}/${templateId}`)
      .then((res) => {
        setDetail(res.data);
        setParams(res.data.template.parameters);
      })
      .catch(() => setError('No se pudo cargar la plantilla.'));
  }, [base, templateId]);

  useEffect(() => {
    setDetail(null);
    setError(null);
    setNotice(null);
    load();
  }, [load]);

  const t = detail?.template;
  const st = t ? statusOf(t.status) : null;
  const editable = t && ['APPROVED', 'REJECTED', 'PAUSED'].includes(t.status);

  const updateParam = (i: number, patch: Partial<TemplateParameter>) => setParams((ps) => ps.map((p, j) => (j === i ? { ...p, ...patch } : p)));

  const saveMapping = async () => {
    if (!t) return;
    setBusy(true);
    setError(null);
    try {
      await axios.put(`${base}/${t.id}/parameters`, params);
      setNotice('Mapeo guardado.');
      load();
      onChanged();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo guardar el mapeo.');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!t || !window.confirm(`¿Eliminar ${t.name} (${t.language}) en Meta? No se puede deshacer.`)) return;
    setBusy(true);
    setError(null);
    try {
      await axios.delete(`${base}/${t.id}`);
      onChanged();
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo eliminar.');
    } finally {
      setBusy(false);
    }
  };

  const samples = Object.fromEntries(
    params.filter((p) => p.source === 'field' && p.field).map((p) => [p.field!, p.example || fields.find((f) => f.key === p.field)?.example || p.field!])
  );

  return (
    <Drawer anchor="right" open={!!templateId} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 640 } } }}>
      {!detail && !error && <LinearProgress />}
      {t && st && (
        <Box sx={{ p: 3 }}>
          <Typography variant="overline" color="text.secondary">
            Plantilla de WhatsApp
          </Typography>
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}>
            <Box>
              <Typography variant="h5">{t.name}</Typography>
              <Typography variant="body2" color="text.secondary">
                {t.language} · {CATEGORY_LABEL[t.category] ?? t.category} · versión {t.version}
                {t.lastSyncedAt ? ` · sincronizada ${new Date(t.lastSyncedAt).toLocaleString('es-MX')}` : ''}
              </Typography>
            </Box>
            <Stack direction="row" spacing={1}>
              <Chip label={st.label} color={st.color} variant="soft" />
              {t.qualityScore && <Chip label={`Calidad ${QUALITY[t.qualityScore]?.label ?? t.qualityScore}`} color={QUALITY[t.qualityScore]?.color ?? 'default'} variant="outlined" />}
            </Stack>
          </Stack>

          {t.status === 'REJECTED' && (
            <Alert severity="error" sx={{ mt: 2 }}>
              Meta la rechazó{t.rejectedReason ? `: ${t.rejectedReason}` : ''}. Puedes editarla y volver a enviarla.
            </Alert>
          )}
          {t.status === 'PENDING' && (
            <Alert severity="warning" sx={{ mt: 2 }}>
              Meta la está revisando (suele tardar minutos, a veces hasta 24 h). Las campañas solo pueden usar plantillas aprobadas.
            </Alert>
          )}

          <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
            <Button size="small" variant="outlined" disabled={!editable || busy} startIcon={<Iconify icon="mdi:pencil-outline" />} onClick={() => onEdit(t)}>
              Editar
            </Button>
            <Button size="small" color="error" disabled={busy || t.status === 'DELETED'} startIcon={<Iconify icon="mdi:delete-outline" />} onClick={remove}>
              Eliminar
            </Button>
          </Stack>

          <Typography variant="subtitle1" sx={{ mt: 3, mb: 1 }}>
            Vista previa
          </Typography>
          <TemplateBubble template={{ components: t.components, parameters: params }} variables={samples} />

          <Typography variant="subtitle1" sx={{ mt: 3 }}>
            Variables
          </Typography>
          <Typography variant="caption" color="text.secondary">
            De dónde sale cada valor al enviar la plantilla a un contacto de una campaña.
          </Typography>
          {slots(t.components).length === 0 ? (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              Esta plantilla no tiene variables.
            </Typography>
          ) : (
            <Stack spacing={1.5} sx={{ mt: 1.5 }}>
              {params.map((p, i) => (
                <Stack key={`${p.component}-${p.key}`} direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
                  <Typography variant="body2" sx={{ minWidth: 150 }}>
                    {componentLabel(p.component)} <b>{p.component === 'header_media' ? '' : `{{${p.key}}}`}</b>
                  </Typography>
                  <TextField select size="small" value={p.source} onChange={(e) => updateParam(i, { source: e.target.value })} sx={{ minWidth: 130 }}>
                    <MenuItem value="field">Dato del cliente</MenuItem>
                    <MenuItem value="static">Valor fijo</MenuItem>
                  </TextField>
                  {p.source === 'field' ? (
                    <TextField select size="small" value={p.field ?? ''} onChange={(e) => updateParam(i, { field: e.target.value })} fullWidth>
                      {fields.map((f) => (
                        <MenuItem key={f.key} value={f.key}>
                          {f.label}
                        </MenuItem>
                      ))}
                    </TextField>
                  ) : (
                    <TextField
                      size="small"
                      placeholder={p.component === 'header_media' ? 'URL del archivo' : 'Valor'}
                      value={p.staticValue ?? ''}
                      onChange={(e) => updateParam(i, { staticValue: e.target.value })}
                      fullWidth
                    />
                  )}
                </Stack>
              ))}
              <Box>
                <Button size="small" variant="contained" disabled={busy} onClick={saveMapping}>
                  Guardar mapeo
                </Button>
              </Box>
            </Stack>
          )}
          {notice && (
            <Alert severity="success" sx={{ mt: 2 }}>
              {notice}
            </Alert>
          )}
          {error && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {error}
            </Alert>
          )}

          <Typography variant="subtitle1" sx={{ mt: 3, mb: 1 }}>
            Versiones
          </Typography>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Versión</TableCell>
                <TableCell>Origen</TableCell>
                <TableCell>Estado</TableCell>
                <TableCell>Fecha</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {detail.versions.map((v) => (
                <TableRow key={v.id}>
                  <TableCell>v{v.version}</TableCell>
                  <TableCell>{SOURCE_LABEL[v.source] ?? v.source}</TableCell>
                  <TableCell>{statusOf(v.status).label}</TableCell>
                  <TableCell>{new Date(v.createdAt).toLocaleString('es-MX')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          <Typography variant="subtitle1" sx={{ mt: 3, mb: 1 }}>
            Historial de estados
          </Typography>
          <Stack spacing={0.5}>
            {[...t.statusHistory].reverse().map((h, i) => (
              <Typography key={i} variant="body2">
                <b>{statusOf(h.status).label}</b> · {SOURCE_LABEL[h.source] ?? h.source} · {new Date(h.at).toLocaleString('es-MX')}
                {h.reason ? ` · ${h.reason}` : ''}
              </Typography>
            ))}
          </Stack>

          <Divider sx={{ my: 3 }} />
          <Button fullWidth variant="outlined" onClick={onClose}>
            Cerrar
          </Button>
        </Box>
      )}
      {error && !t && (
        <Alert severity="error" sx={{ m: 3 }}>
          {error}
        </Alert>
      )}
    </Drawer>
  );
}
