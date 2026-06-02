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
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';
import { normalizeToolLabel } from 'src/aiagentflow/utils/toolLabels';

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

const HIDDEN_INTERNAL_TOOL_PATTERNS = [
  /^af_(list|get|preview|draft|refine|validate|create|update|publish|pause|resume|run|retry)_campaign/i,
  /^af_.*campaign.*(playbook|outcome|segment|run|contact)/i,
];

const isVisibleOperationalTool = (tool: ToolRow) =>
  !HIDDEN_INTERNAL_TOOL_PATTERNS.some((pattern) => pattern.test(tool.name));

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
      renderCell: (params: any) => normalizeToolLabel(params.value),
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
          title="Herramientas y conexiones"
          description="Administra capacidades disponibles para asistentes, automatizaciones y conexiones externas."
          icon="mdi:tools"
          help={<TermHelp title="Aqui administras herramientas disponibles para los asistentes y conexiones con sistemas externos." />}
          actions={<Button variant="outlined" onClick={fetchTools}>Actualizar</Button>}
        />

        <Grid container spacing={2} sx={{ mb: 3 }}>
          {[
            ['Integraciones', 'Administra conexiones instaladas y credenciales', paths.dashboard.marketplace, 'mdi:storefront-outline'],
            ['Canales', 'Conecta WhatsApp, web chat y APIs', paths.dashboard.system.channels, 'mdi:message-processing-outline'],
            ['Conectores avanzados', 'Habilita conectores y herramientas avanzadas para asistentes', paths.dashboard.system.mcp, 'mdi:connection'],
          ].map(([title, subtitle, href, icon]) => (
            <Grid item xs={12} md={4} key={title}>
              <Card
                component={RouterLink}
                href={href}
                sx={{ p: 2, textDecoration: 'none', color: 'inherit', height: '100%' }}
              >
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Iconify icon={icon} width={28} sx={{ color: 'primary.main' }} />
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

