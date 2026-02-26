import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import axios from 'src/lib/axios';
import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

interface McpServer {
  name: string;
  transport: string;
  url?: string;
  securityMode: string;
}

interface McpTool {
  name: string;
  description?: string;
  inputSchemaJson?: string;
}

export default function McpPage() {
  const [servers, setServers] = useState<McpServer[]>([]);
  const [selectedServer, setSelectedServer] = useState('');
  const [tools, setTools] = useState<McpTool[]>([]);
  const [selectedTool, setSelectedTool] = useState('');
  const [inputJson, setInputJson] = useState('{}');
  const [output, setOutput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadServers = async () => {
    setError(null);
    try {
      const res = await axios.get('/api/v1/mcp/servers');
      setServers(res.data ?? []);
      if (res.data?.length && !selectedServer) setSelectedServer(res.data[0].name);
    } catch (e: any) {
      setError(e?.message || 'Failed to load MCP servers');
    }
  };

  useEffect(() => {
    loadServers();
  }, []);

  const loadTools = async () => {
    if (!selectedServer) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get(`/api/v1/mcp/servers/${selectedServer}/tools`);
      const data = Array.isArray(res.data) ? res.data : [];
      setTools(data);
      if (data.length) {
        setSelectedTool(data[0].name);
        setInputJson(data[0].inputSchemaJson || '{}');
      }
    } catch (e: any) {
      setError(e?.message || 'Failed to load MCP tools');
      setTools([]);
    } finally {
      setLoading(false);
    }
  };

  const invoke = async () => {
    if (!selectedServer || !selectedTool) return;
    setLoading(true);
    setError(null);
    try {
      const res = await axios.post(`/api/v1/mcp/servers/${selectedServer}/invoke`, {
        toolName: selectedTool,
        inputJson,
      });
      setOutput(JSON.stringify(res.data, null, 2));
    } catch (e: any) {
      setError(e?.message || 'Failed to invoke MCP tool');
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <Helmet>
        <title>MCP Console | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 4 }}>
          <Typography variant="h4">MCP Console</Typography>
          <Typography variant="body2" color="text.secondary">
            Discover tools from configured MCP servers and invoke them with real payloads.
          </Typography>
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} action={<Button color="inherit" size="small" onClick={loadServers}>Retry</Button>}>
            {error}
          </Alert>
        )}

        <Grid container spacing={3}>
          <Grid item xs={12} md={5}>
            <Card>
              <CardContent>
                <Stack spacing={2}>
                  <TextField
                    select
                    label="MCP Server"
                    value={selectedServer}
                    onChange={(e) => setSelectedServer(e.target.value)}
                    fullWidth
                  >
                    {servers.map((s) => (
                      <MenuItem key={s.name} value={s.name}>{s.name} ({s.transport})</MenuItem>
                    ))}
                  </TextField>

                  <Stack direction="row" spacing={1}>
                    <Button variant="outlined" onClick={loadServers} disabled={loading}>
                      Refresh Servers
                    </Button>
                    <Button variant="outlined" onClick={loadTools} disabled={loading || !selectedServer}>
                      Discover Tools
                    </Button>
                  </Stack>

                  <TextField
                    select
                    label="Tool"
                    value={selectedTool}
                    onChange={(e) => setSelectedTool(e.target.value)}
                    fullWidth
                  >
                    {tools.map((t) => (
                      <MenuItem key={t.name} value={t.name}>{t.name}</MenuItem>
                    ))}
                  </TextField>

                  <TextField
                    label="Input JSON"
                    value={inputJson}
                    onChange={(e) => setInputJson(e.target.value)}
                    fullWidth
                    multiline
                    minRows={8}
                  />

                  <Button variant="contained" onClick={invoke} disabled={loading || !selectedTool}>
                    Invoke Tool
                  </Button>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={7}>
            <Card>
              <CardContent>
                <Typography variant="subtitle1" sx={{ mb: 1 }}>Result</Typography>
                <TextField value={output} fullWidth multiline minRows={22} InputProps={{ readOnly: true }} />
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}
