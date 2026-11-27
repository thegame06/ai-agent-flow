# WhatsApp QR Bridge (AgentFlow)

Bridge para habilitar modo QR (WhatsApp Web) y conectar con AgentFlow.

## Variables

- `PORT` (default `3401`)
- `AGENTFLOW_BASE_URL` (ej. `http://localhost:5000`)
- `TENANT_ID` (tenant para webhook QR)
- `BRIDGE_API_KEY` (opcional)
- `ALLOWED_NUMBERS` (recomendado al vincular un WhatsApp personal): teléfonos separados por coma. Solo se reenvían mensajes de esos números y solo se les puede escribir a ellos. Los grupos, estados y difusiones nunca se reenvían.

## Uso

```bash
cd tools/whatsapp-qr-bridge
npm install
PORT=3401 AGENTFLOW_BASE_URL=http://localhost:5000 TENANT_ID=demo npm start
```

## Endpoints

- `POST /session/start` body `{ channelId }`
- `GET /session/qr?channelId=...`
- `GET /session/status?channelId=...`
- `GET /health`
- `POST /messages/send` body `{ channelId, to, content }`
- `POST /session/disconnect` body `{ channelId }`

## AgentFlow config requerida (WhatsAppOptions)

- `QrBridgeBaseUrl` = `http://localhost:3401`
- `QrBridgeApiKey` = `<token>` (si habilitado)

## Nota

Este modo QR no reemplaza WhatsApp Business API para producción enterprise.
Úsalo para pruebas internas y validación sin cuenta Business.
