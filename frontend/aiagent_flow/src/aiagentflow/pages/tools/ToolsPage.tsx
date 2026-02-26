import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';

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

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Label } from 'src/components/label';

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

  const fetchTools = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(`/api/v1/tenants/${tenantId}/tools/status`);
      setTools(res.data ?? []);
    } catch (err: any) {
      setError(err?.message || 'Failed to load tools');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchTools();
  }, [tenantId]);

  const toggleTool = async (row: ToolRow, enabled: boolean) => {
    try {
      await axios.put(`/api/v1/tenants/${tenantId}/tools/${row.name}/enabled`, { enabled });
      await fetchTools();
    } catch (err: any) {
      setError(err?.message || 'Failed to update tool status');
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
      setError(err?.response?.data?.errorMessage || err?.message || 'Tool invoke failed');
    } finally {
      setRunLoading(false);
    }
  };

  const columns: any[] = [
    { field: 'name', headerName: 'Tool', minWidth: 190, flex: 1 },
    { field: 'version', headerName: 'Version', width: 120 },
    {
      field: 'riskLevel',
      headerName: 'Risk',
      width: 120,
      renderCell: (params: any) => <Label variant="soft">{params.value}</Label>,
    },
    {
      field: 'health',
      headerName: 'Health',
      width: 140,
      renderCell: (params: any) => (
        <Label variant="soft" color={params.value === 'Healthy' ? 'success' : 'error'}>
          {params.value}
        </Label>
      ),
    },
    {
      field: 'enabled',
      headerName: 'Enabled',
      width: 130,
      renderCell: (params: any) => (
        <Switch checked={!!params.value} onChange={(e) => toggleTool(params.row, e.target.checked)} />
      ),
    },
  ];

  return (
    <>
      <Helmet>
        <title>Extensions & Tools | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Platform Tools & Extensions</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              Tenant-aware tool status, health, and invoke testing.
            </Typography>
          </Box>
          <Button variant="outlined" onClick={fetchTools}>Refresh</Button>
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} action={<Button color="inherit" size="small" onClick={fetchTools}>Retry</Button>}>
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
                    {selectedTool ? `Tool Test: ${selectedTool.name}` : 'Select a tool to test'}
                  </Typography>

                  <TextField
                    label="Input JSON"
                    value={inputJson}
                    onChange={(e) => setInputJson(e.target.value)}
                    fullWidth
                    multiline
                    minRows={8}
                    disabled={!selectedTool}
                  />

                  <Button variant="contained" onClick={runToolTest} disabled={!selectedTool || runLoading}>
                    Invoke Tool
                  </Button>

                  <TextField label="Result" value={result} fullWidth multiline minRows={12} InputProps={{ readOnly: true }} />
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
