import express from 'express';

const app = express();
app.use(express.json({ limit: '1mb' }));

const PORT = Number(process.env.PORT || 3501);
const API_KEY = process.env.MCP_TEST_API_KEY || '';

function auth(req, res, next) {
  if (!API_KEY) return next();
  const token = req.headers.authorization?.replace('Bearer ', '');
  if (token !== API_KEY) return res.status(401).json({ error: 'unauthorized' });
  next();
}

app.use(auth);

app.get('/tools', (_req, res) => {
  res.json([
    {
      name: 'health_check',
      description: 'Returns server health and timestamp',
      inputSchemaJson: '{}'
    },
    {
      name: 'echo_payload',
      description: 'Echoes request metadata and input',
      inputSchemaJson: '{"type":"object"}'
    }
  ]);
});

app.post('/invoke', (req, res) => {
  const { tool, tenantId, executionId, inputJson, metadata } = req.body || {};

  if (!tool) return res.status(400).json({ error: 'tool is required' });

  if (tool === 'health_check') {
    return res.json({
      ok: true,
      tool,
      tenantId,
      executionId,
      timestamp: new Date().toISOString(),
      status: 'healthy'
    });
  }

  if (tool === 'echo_payload') {
    return res.json({
      ok: true,
      tool,
      tenantId,
      executionId,
      inputJson,
      metadata
    });
  }

  return res.status(404).json({ error: `unknown tool '${tool}'` });
});

app.get('/health', (_req, res) => {
  res.json({ ok: true, service: 'mcp-test-server' });
});

app.listen(PORT, () => {
  console.log(`MCP test server running on :${PORT}`);
});
