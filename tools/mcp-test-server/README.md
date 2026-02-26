# MCP Test Server (real HTTP)

Servidor MCP mínimo para validar discovery + invoke en AgentFlow sin rutas simuladas en core.

## Run

```bash
cd tools/mcp-test-server
npm install
PORT=3501 npm start
```

Con auth opcional:

```bash
MCP_TEST_API_KEY=supersecret PORT=3501 npm start
```

## Endpoints

- `GET /tools`
- `POST /invoke`
- `GET /health`

## AgentFlow config example

```json
{
  "Mcp": {
    "Servers": [
      {
        "Name": "local-test",
        "Transport": "Http",
        "Url": "http://localhost:3501/invoke",
        "Security": {
          "Mode": "Open",
          "EnableAuditLogs": true,
          "AuthSecretName": "MCP_TEST_API_KEY"
        }
      }
    ]
  }
}
```

> Discovery uses `GET {Url-without-last-segment}/tools` via `McpDiscoveryService`.
