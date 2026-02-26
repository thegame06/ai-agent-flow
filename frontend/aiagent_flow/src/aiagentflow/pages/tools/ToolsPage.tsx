import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import { DataGrid } from '@mui/x-data-grid';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { useTools } from './Hooks/useTools';
import { TOOL_COLUMNS } from './Config/Columns';

// ----------------------------------------------------------------------

export default function ToolsPage() {
  const { tools, loading } = useTools();
  const [selectedTool, setSelectedTool] = useState<any | null>(null);
  const [inputJson, setInputJson] = useState('{}');
  const [result, setResult] = useState('');
  const [runLoading, setRunLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  return (
    <>
      <Helmet>
        <title>Extensions & Tools | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 5 }}>
          <Typography variant="h4">Platform Tools & Extensions</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            Registered capabilities available for your agents.
          </Typography>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={3}>
          <Grid item xs={12} md={7}>
            <Card sx={{ height: 620, width: '100%' }}>
              <DataGrid
                rows={tools}
                columns={TOOL_COLUMNS}
                loading={loading}
                getRowId={(row) => row.name + row.version}
                pageSizeOptions={[10, 25, 50]}
                sx={{ border: 0 }}
                onRowClick={(params) => {
                  setSelectedTool(params.row);
                  setResult('');
                  setInputJson(params.row?.inputSchemaJson || '{}');
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

                  <Button
                    variant="contained"
                    onClick={runToolTest}
                    disabled={!selectedTool || runLoading}
                  >
                    Invoke Tool
                  </Button>

                  <TextField
                    label="Result"
                    value={result}
                    fullWidth
                    multiline
                    minRows={12}
                    InputProps={{ readOnly: true }}
                  />
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
