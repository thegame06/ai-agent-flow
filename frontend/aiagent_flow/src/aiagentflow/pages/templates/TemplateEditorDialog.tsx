import { useRef, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { TemplateBubble } from './TemplateBubble';

import type { WaTemplate, ChannelMeta, TemplateField, TemplateButton, TemplateParameter, TemplateComponent } from './templateModel';

// ----------------------------------------------------------------------

type Draft = {
  channelId: string;
  name: string;
  language: string;
  category: string;
  headerText: string;
  body: string;
  footer: string;
  buttons: TemplateButton[];
};

const LANGUAGES = [
  { code: 'es', label: 'Español' },
  { code: 'es_MX', label: 'Español (México)' },
  { code: 'es_AR', label: 'Español (Argentina)' },
  { code: 'es_ES', label: 'Español (España)' },
  { code: 'en_US', label: 'Inglés (EE. UU.)' },
  { code: 'pt_BR', label: 'Portugués (Brasil)' },
];

type Props = {
  base: string;
  open: boolean;
  template: WaTemplate | null; // null = new
  /** Business channels; the template is created in the Meta account of the chosen one. */
  channels: ChannelMeta[];
  defaultChannelId?: string;
  fields: TemplateField[];
  onClose: () => void;
  onSaved: () => void;
};

/** Field placeholders as authors write them ({{primer_nombre}}); for an existing template, numbers go back to field names. */
function toAuthorText(text: string | null | undefined, component: string, parameters: TemplateParameter[]) {
  return (text ?? '').replace(/\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g, (m, key: string) => {
    const p = parameters.find((x) => x.component === component && x.key === key);
    return p?.source === 'field' && p.field ? `{{${p.field}}}` : m;
  });
}

function fromTemplate(t: WaTemplate | null, channels: ChannelMeta[], defaultChannelId?: string): Draft {
  if (!t)
    return {
      channelId: defaultChannelId || channels.find((c) => c.hasToken)?.channelId || channels[0]?.channelId || '',
      name: '',
      language: 'es',
      category: 'UTILITY',
      headerText: '',
      body: '',
      footer: '',
      buttons: [],
    };
  const find = (type: string) => t.components.find((c) => c.type.toUpperCase() === type);
  return {
    channelId: channels.find((c) => c.businessAccountId === t.businessAccountId)?.channelId ?? '',
    name: t.name,
    language: t.language,
    category: t.category,
    headerText: toAuthorText(find('HEADER')?.text, 'header', t.parameters),
    body: toAuthorText(find('BODY')?.text, 'body', t.parameters),
    footer: find('FOOTER')?.text ?? '',
    buttons: (find('BUTTONS')?.buttons ?? []).map((b) => ({ ...b })),
  };
}

export function TemplateEditorDialog({ base, open, template, channels, defaultChannelId, fields, onClose, onSaved }: Props) {
  const [draft, setDraft] = useState<Draft>(() => fromTemplate(template, channels, defaultChannelId));
  const [target, setTarget] = useState<'body' | 'headerText'>('body');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bodyRef = useRef<HTMLTextAreaElement | null>(null);

  useEffect(() => {
    if (open) {
      setDraft(fromTemplate(template, channels, defaultChannelId));
      setError(null);
    }
  }, [open, template, channels, defaultChannelId]);

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }));

  const insertField = (key: string) => {
    const token = `{{${key}}}`;
    if (target === 'headerText') {
      set('headerText', `${draft.headerText}${token}`);
      return;
    }
    const el = bodyRef.current;
    const start = el?.selectionStart ?? draft.body.length;
    const end = el?.selectionEnd ?? draft.body.length;
    set('body', `${draft.body.slice(0, start)}${token}${draft.body.slice(end)}`);
  };

  // Live preview with each field's sample value.
  const previewComponents: TemplateComponent[] = [
    ...(draft.headerText ? [{ type: 'HEADER', format: 'TEXT', text: draft.headerText }] : []),
    { type: 'BODY', text: draft.body },
    ...(draft.footer ? [{ type: 'FOOTER', text: draft.footer }] : []),
    ...(draft.buttons.length ? [{ type: 'BUTTONS', buttons: draft.buttons }] : []),
  ];
  const samples = Object.fromEntries(fields.map((f) => [f.key, f.example]));
  const previewParams: TemplateParameter[] = fields.flatMap((f) => [
    { component: 'header', key: f.key, source: 'field', field: f.key, example: f.example },
    { component: 'body', key: f.key, source: 'field', field: f.key, example: f.example },
  ]);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const payload = { ...draft, buttons: draft.buttons.filter((b) => b.text.trim()) };
      if (template) await axios.put(`${base}/${template.id}`, payload);
      else await axios.post(base, payload);
      onSaved();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo guardar la plantilla.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg">
      <DialogTitle>{template ? `Editar ${template.name}` : 'Nueva plantilla de WhatsApp'}</DialogTitle>
      <DialogContent>
        <Box sx={{ display: 'grid', gap: 3, gridTemplateColumns: { xs: '1fr', md: '3fr 2fr' }, pt: 1 }}>
          <Stack spacing={2}>
            {template ? (
              <Alert severity="info">
                Meta revisa de nuevo la plantilla al editarla. Mientras tanto queda «En revisión» y no se puede usar en
                campañas.
              </Alert>
            ) : (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField
                  label="Nombre"
                  value={draft.name}
                  onChange={(e) => set('name', e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_'))}
                  helperText="Minúsculas, números y guion bajo. Ej. recordatorio_pago"
                  fullWidth
                />
                <TextField
                  select
                  label="Canal"
                  value={draft.channelId}
                  onChange={(e) => set('channelId', e.target.value)}
                  helperText={
                    channels.find((c) => c.channelId === draft.channelId)?.provider === 'twilio'
                      ? 'Se crea en Twilio y se envía a revisión de WhatsApp. Encabezado y pie van dentro del texto; solo botones de respuesta rápida.'
                      : 'La plantilla se crea en la cuenta de Meta de este número.'
                  }
                  sx={{ minWidth: 240 }}
                >
                  {channels.map((c) => (
                    <MenuItem key={c.channelId} value={c.channelId} disabled={c.provider !== 'twilio' && !c.hasToken}>
                      {c.provider === 'twilio' ? `${c.channelName} · Twilio` : `${c.channelName} · WABA ${c.businessAccountId ?? '—'}`}
                      {c.provider !== 'twilio' && !c.hasToken ? ' (sin token)' : ''}
                    </MenuItem>
                  ))}
                </TextField>
              </Stack>
            )}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField select label="Idioma" value={draft.language} onChange={(e) => set('language', e.target.value)} disabled={!!template} fullWidth>
                {LANGUAGES.map((l) => (
                  <MenuItem key={l.code} value={l.code}>
                    {l.label}
                  </MenuItem>
                ))}
              </TextField>
              <TextField
                select
                label="Categoría"
                value={draft.category}
                onChange={(e) => set('category', e.target.value)}
                disabled={!!template && template.status !== 'REJECTED'}
                helperText="Cobranza y avisos: Utilidad. Promociones: Marketing."
                fullWidth
              >
                <MenuItem value="UTILITY">Utilidad</MenuItem>
                <MenuItem value="MARKETING">Marketing</MenuItem>
                <MenuItem value="AUTHENTICATION">Autenticación</MenuItem>
              </TextField>
            </Stack>
            <TextField
              label="Encabezado (opcional)"
              value={draft.headerText}
              onChange={(e) => set('headerText', e.target.value)}
              onFocus={() => setTarget('headerText')}
              inputProps={{ maxLength: 60 }}
              helperText="Hasta 60 caracteres y una variable."
            />
            <TextField
              label="Mensaje"
              value={draft.body}
              onChange={(e) => set('body', e.target.value)}
              onFocus={() => setTarget('body')}
              inputRef={bodyRef}
              multiline
              minRows={5}
              inputProps={{ maxLength: 1024 }}
              helperText={`${draft.body.length}/1024. No puede empezar ni terminar con una variable.`}
            />
            <Box>
              <Typography variant="caption" color="text.secondary">
                Insertar dato del cliente en el {target === 'headerText' ? 'encabezado' : 'mensaje'}:
              </Typography>
              <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
                {fields.map((f) => (
                  <Chip key={f.key} size="small" variant="outlined" label={f.label} title={`Ej. ${f.example}`} onClick={() => insertField(f.key)} />
                ))}
              </Stack>
            </Box>
            <TextField label="Pie (opcional)" value={draft.footer} onChange={(e) => set('footer', e.target.value)} inputProps={{ maxLength: 60 }} />

            <Box>
              <Typography variant="subtitle2">Botones</Typography>
              <Stack spacing={1} sx={{ mt: 1 }}>
                {draft.buttons.map((b, i) => (
                  <Stack key={i} direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
                    <TextField
                      select
                      size="small"
                      value={b.type}
                      onChange={(e) => set('buttons', draft.buttons.map((x, j) => (j === i ? { ...x, type: e.target.value } : x)))}
                      sx={{ minWidth: 170 }}
                    >
                      <MenuItem value="QUICK_REPLY">Respuesta rápida</MenuItem>
                      <MenuItem value="URL">Enlace</MenuItem>
                      <MenuItem value="PHONE_NUMBER">Llamar</MenuItem>
                    </TextField>
                    <TextField
                      size="small"
                      label="Texto"
                      value={b.text}
                      onChange={(e) => set('buttons', draft.buttons.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)))}
                      inputProps={{ maxLength: 25 }}
                      fullWidth
                    />
                    {b.type === 'URL' && (
                      <TextField
                        size="small"
                        label="URL"
                        value={b.url ?? ''}
                        onChange={(e) => set('buttons', draft.buttons.map((x, j) => (j === i ? { ...x, url: e.target.value } : x)))}
                        fullWidth
                      />
                    )}
                    {b.type === 'PHONE_NUMBER' && (
                      <TextField
                        size="small"
                        label="Teléfono"
                        value={b.phoneNumber ?? ''}
                        onChange={(e) => set('buttons', draft.buttons.map((x, j) => (j === i ? { ...x, phoneNumber: e.target.value } : x)))}
                        fullWidth
                      />
                    )}
                    <IconButton aria-label="Quitar botón" onClick={() => set('buttons', draft.buttons.filter((_, j) => j !== i))}>
                      <Iconify icon="mdi:delete-outline" />
                    </IconButton>
                  </Stack>
                ))}
                {draft.buttons.length < 10 && (
                  <Box>
                    <Button size="small" startIcon={<Iconify icon="mdi:plus" />} onClick={() => set('buttons', [...draft.buttons, { type: 'QUICK_REPLY', text: '' }])}>
                      Agregar botón
                    </Button>
                  </Box>
                )}
              </Stack>
            </Box>
          </Stack>

          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Vista previa
            </Typography>
            <TemplateBubble template={{ components: previewComponents, parameters: previewParams }} variables={samples} />
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
              Con datos de ejemplo. Al guardar, las variables se numeran ({'{{1}}'}, {'{{2}}'}…) como pide WhatsApp y cada
              número queda ligado a su dato.
            </Typography>
          </Box>
        </Box>
        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={busy || !draft.body.trim() || (!template && !draft.name)} onClick={save}>
          {template ? 'Enviar cambios a revisión' : 'Enviar a revisión'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
