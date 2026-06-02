import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import Button from '@mui/material/Button';
import { DataGrid } from '@mui/x-data-grid';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { TermHelp } from 'src/aiagentflow/components/TermHelp';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { normalizeToolLabel } from 'src/aiagentflow/utils/toolLabels';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';

interface ToolRow {
  name: string;
  version: string;
  description?: string;
  riskLevel: string;
  enabled: boolean;
  health: string;
  message?: string;
  checkedAt?: string;
  inputSchemaJson?: string;
}

const isVisibleOperationalTool = (tool: ToolRow) => !!tool?.name;

const toolIcon = (toolName?: string) => {
  const name = String(toolName || '').toLowerCase();
  if (name.includes('campaign')) return 'mdi:bullhorn-variant-outline';
  if (name.includes('invoice') || name.includes('payment') || name.includes('billing')) return 'mdi:cash-multiple';
  if (name.includes('sale') || name.includes('commerce') || name.includes('product')) return 'mdi:cart-outline';
  if (name.includes('channel') || name.includes('message') || name.includes('whatsapp')) return 'mdi:message-processing-outline';
  if (name.includes('voice') || name.includes('call')) return 'mdi:phone-in-talk-outline';
  if (name.includes('agent') || name.includes('assistant')) return 'mdi:robot-outline';
  if (name.includes('workflow') || name.includes('automation')) return 'mdi:source-branch';
  if (name.includes('tool') || name.includes('mcp')) return 'mdi:puzzle-outline';
  return 'mdi:wrench-outline';
};

function schemaToPayload(schemaJson?: string) {
  if (!schemaJson) return '{}';
  try {
    const s = JSON.parse(schemaJson);
    if (!s?.properties) return schemaJson;
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries<any>(s.properties)) {
      if (Array.isArray(v?.enum) && v.enum.length) out[k] = v.enum[0];
      else if (v?.type === 'number' || v?.type === 'integer') out[k] = 0;
      else if (v?.type === 'boolean') out[k] = false;
      else if (v?.type === 'array') out[k] = [];
      else if (v?.type === 'object') out[k] = {};
      else out[k] = '';
    }
    return JSON.stringify(out, null, 2);
  } catch {
    return schemaJson;
  }
}

export default function ToolsPage() {
  const tenantId = useTenantId();
  const [tools, setTools] = useState<ToolRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedTool, setSelectedTool] = useState<ToolRow | null>(null);
  const [inputJson, setInputJson] = useState('{}');
  const [result, setResult] = useState('');
  const [runLoading, setRunLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchTools = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(`/api/v1/tenants/${tenantId}/tools/status`);
      setTools(((res.data ?? []) as ToolRow[]).filter(isVisibleOperationalTool));
    } catch (err: any) {
      setError(err?.message || 'No se pudieron cargar las herramientas');
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    fetchTools();
  }, [fetchTools]);

  const toggleTool = async (row: ToolRow, enabled: boolean) => {
    try {
      await axios.put(`/api/v1/tenants/${tenantId}/tools/${row.name}/enabled`, { enabled });
      await fetchTools();
    } catch (err: any) {
      setError(err?.message || 'No se pudo actualizar el estado de la herramienta');
    }
  };

  const runToolTest = async () => {
    if (!selectedTool) return;
    setRunLoading(true);
    setError(null);
    try {
      const res = await axios.post(`/api/v1/extensions/tools/${selectedTool.name}/invoke`, {
        inputJson,
      });
      setResult(JSON.stringify(res.data, null, 2));
    } catch (err: any) {
      setError(err?.response?.data?.errorMessage || err?.message || 'La prueba de la herramienta fallo');
    } finally {
      setRunLoading(false);
    }
  };

  const columns: any[] = [
    {
      field: 'name',
      headerName: 'Herramienta',
      minWidth: 190,
      flex: 1,
      renderCell: (params: any) => (
        <Stack direction="row" spacing={1.25} alignItems="center" sx={{ minWidth: 0 }}>
          <Box
            sx={{
              width: 32,
              height: 32,
              borderRadius: 1.5,
              display: 'grid',
              placeItems: 'center',
              bgcolor: 'success.lighter',
              color: 'success.darker',
              flexShrink: 0,
            }}
          >
            <Iconify icon={toolIcon(params.row?.name)} width={18} />
          </Box>
          <Typography variant="body2" noWrap>
            {normalizeToolLabel(params.value)}
          </Typography>
        </Stack>
      ),
    },
    { field: 'version', headerName: 'Version', width: 120 },
    {
      field: 'riskLevel',
      headerName: 'Riesgo',
      width: 120,
      renderCell: (params: any) => <Label variant="soft">{params.value}</Label>,
    },
    {
      field: 'health',
      headerName: 'Estado',
      width: 140,
      renderCell: (params: any) => (
        <Label variant="soft" color={params.value === 'Healthy' ? 'success' : 'error'}>
          {params.value}
        </Label>
      ),
    },
    {
      field: 'enabled',
      headerName: 'Activa',
      width: 130,
      renderCell: (params: any) => (
        <Switch checked={!!params.value} onChange={(e) => toggleTool(params.row, e.target.checked)} />
      ),
    },
  ];

  return (
    <>
      <Helmet>
        <title>Herramientas y conexiones | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <BrandPageHeader
          eyebrow="Capacidades operativas"
          title="Herramientas operativas"
          description="Administra capacidades usadas por campañas, asistentes y automatizaciones. Aquí se prueban y activan herramientas del producto."
          icon="mdi:tools"
          help={<TermHelp title="Aqui administras herramientas disponibles para campañas, asistentes y automatizaciones. Las integraciones viven aparte y no se instalan desde esta vista." />}
          actions={<Button variant="outlined" onClick={fetchTools}>Actualizar</Button>}
        />

        <Grid container spacing={2} sx={{ mb: 3 }}>
          {[
            ['Campañas', 'Usa estas herramientas desde salidas comerciales y follow-ups', paths.dashboard.campaigns, 'mdi:bullhorn-variant-outline'],
            ['Canales', 'Vincula herramientas a WhatsApp, voz, web chat y APIs', paths.dashboard.system.channels, 'mdi:message-processing-outline'],
            ['Conectores avanzados', 'Amplía automatizaciones y asistentes con conectores especializados', paths.dashboard.system.mcp, 'mdi:connection'],
          ].map(([title, subtitle, href, icon]) => (
            <Grid item xs={12} md={4} key={title}>
              <Card
                component={RouterLink}
                href={href}
                sx={{ p: 2, textDecoration: 'none', color: 'inherit', height: '100%' }}
              >
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Box
                    sx={{
                      width: 44,
                      height: 44,
                      borderRadius: 2,
                      display: 'grid',
                      placeItems: 'center',
                      bgcolor: 'success.lighter',
                      color: 'success.darker',
                      flexShrink: 0,
                    }}
                  >
                    <Iconify icon={icon} width={24} />
                  </Box>
                  <Box>
                    <Typography variant="subtitle2">{title}</Typography>
                    <Typography variant="caption" color="text.secondary">{subtitle}</Typography>
                  </Box>
                </Stack>
              </Card>
            </Grid>
          ))}
        </Grid>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} action={<Button color="inherit" size="small" onClick={fetchTools}>Reintentar</Button>}>
            {error}
          </Alert>
        )}

        <Grid container spacing={3}>
          <Grid item xs={12} md={7}>
            <Card sx={{ height: 620, width: '100%' }}>
              <DataGrid
                rows={tools}
                columns={columns}
                loading={loading}
                getRowId={(row) => row.name + row.version}
                pageSizeOptions={[10, 25, 50]}
                sx={{ border: 0 }}
                onRowClick={(params) => {
                  setSelectedTool(params.row as ToolRow);
                  setResult('');
                  setInputJson(schemaToPayload((params.row as any)?.inputSchemaJson));
                }}
              />
            </Card>
          </Grid>

          <Grid item xs={12} md={5}>
            <Card>
              <CardContent>
                <Stack spacing={2}>
                  <Typography variant="subtitle1">
                    {selectedTool ? `Probar herramienta: ${normalizeToolLabel(selectedTool.name)}` : 'Selecciona una herramienta'}
                  </Typography>
                  {selectedTool && (
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Box
                        sx={{
                          width: 36,
                          height: 36,
                          borderRadius: 1.5,
                          display: 'grid',
                          placeItems: 'center',
                          bgcolor: 'success.lighter',
                          color: 'success.darker',
                        }}
                      >
                        <Iconify icon={toolIcon(selectedTool.name)} width={20} />
                      </Box>
                      <Typography variant="body2" color="text.secondary">
                        {selectedTool.description || 'Herramienta lista para prueba manual.'}
                      </Typography>
                    </Stack>
                  )}

                  <TextField
                    label="Entrada JSON"
                    value={inputJson}
                    onChange={(e) => setInputJson(e.target.value)}
                    fullWidth
                    multiline
                    minRows={8}
                    disabled={!selectedTool}
                  />

                  <Button variant="contained" onClick={runToolTest} disabled={!selectedTool || runLoading}>
                    Probar herramienta
                  </Button>

                  <TextField label="Resultado" value={result} fullWidth multiline minRows={12} InputProps={{ readOnly: true }} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}

