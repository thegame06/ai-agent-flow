# Channel Gateway Architecture

## Overview

AgentFlow now includes a **multi-channel gateway** that enables agents to communicate through various channels (WhatsApp, Web Chat, API, Telegram, Slack, etc.) with a unified architecture.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│              AgentFlow Channel Gateway                  │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │   WhatsApp   │  │   Web Chat   │  │   API Direct │ │
│  │   (QR/Biz)   │  │   (Widget)   │  │   (REST)     │ │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘ │
│         │                 │                 │          │
│         └─────────────────┴─────────────────┘          │
│                           ↓                            │
│              ┌─────────────────────────┐               │
│              │   IChannelHandler       │               │
│              │   + ProcessMessage()    │               │
│              │   + SendReply()         │               │
│              │   + GetContext()        │               │
│              └───────────┬─────────────┘               │
│                          ↓                             │
│              ┌─────────────────────────┐               │
│              │   ChannelGateway        │               │
│              │   (Orchestrator)        │               │
│              └───────────┬─────────────┘               │
│                          ↓                             │
│              ┌─────────────────────────┐               │
│              │   Agent Execution       │               │
│              │   Engine                │               │
│              └─────────────────────────┘               │
└─────────────────────────────────────────────────────────┘
```

## Key Concepts

### ChannelDefinition
Configuration for a communication channel:
- `Type`: WhatsApp, WebChat, Api, Telegram, Slack, etc.
- `Config`: Channel-specific settings (API keys, phone IDs, etc.)
- `Status`: Active, Inactive, Error, Maintenance

### ChannelSession
Active conversation session within a channel:
- `Identifier`: Channel-specific user ID (phone, userId, apiKey)
- `AgentId`: Linked agent for this session
- `ThreadId`: Linked conversation thread
- `ExpiresAt`: Session expiration

### ChannelMessage
Normalized message format across all channels:
- `Direction`: Incoming or Outgoing
- `Type`: Text, Image, Audio, Video, Document, etc.
- `Content`: Message content
- `Metadata`: Channel-specific metadata

### ChannelContext
Unified context for agent execution:
- `UserIdentifier`: Unified user ID across channels
- `UserDisplayName`: Display name if available
- `Metadata`: Channel-specific context

## Supported Channels

### WhatsApp
- **Authentication**: QR code (like OpenClaw) or Business API
- **Identifier**: Phone number (+50581143874)
- **Features**: Text, images, documents, interactive messages
- **Setup**: 
  - QR mode: Scan with WhatsApp mobile app
  - Business mode: Meta Business API token + Phone Number ID

### Web Chat
- **Authentication**: User session (logged-in user)
- **Identifier**: User ID from authentication
- **Features**: Text, rich messages, widgets
- **Setup**: Embed widget JavaScript on website

### API Direct
- **Authentication**: API key or system credentials
- **Identifier**: System ID or API key
- **Features**: Full REST API integration
- **Setup**: Configure API credentials

### Telegram (Planned)
- **Authentication**: Bot token
- **Identifier**: Telegram user ID
- **Features**: Text, media, inline keyboards

### Slack (Planned)
- **Authentication**: OAuth bot token
- **Identifier**: Slack user ID
- **Features**: Text, blocks, interactions

## API Endpoints

### Channels
- `GET /api/v1/tenants/{tenantId}/channels` - List all channels
- `POST /api/v1/tenants/{tenantId}/channels` - Create channel
- `POST /api/v1/tenants/{tenantId}/channels/{id}/activate` - Activate channel
- `POST /api/v1/tenants/{tenantId}/channels/{id}/deactivate` - Deactivate channel
- `POST /api/v1/tenants/{tenantId}/channels/{id}/health` - Health check
- `DELETE /api/v1/tenants/{tenantId}/channels/{id}` - Delete channel

### Channel Sessions
- `GET /api/v1/tenants/{tenantId}/channel-sessions` - List active sessions
- `GET /api/v1/tenants/{tenantId}/channel-sessions/{id}` - Get session details
- `POST /api/v1/tenants/{tenantId}/channel-sessions/{id}/close` - Close session
- `GET /api/v1/tenants/{tenantId}/channel-sessions/{id}/messages` - Get messages

### Webhooks
- `POST /api/v1/tenants/{tenantId}/channels/whatsapp/webhook` - WhatsApp webhook
- `POST /api/v1/tenants/{tenantId}/channels/telegram/webhook` - Telegram webhook

## Configuration Example

### WhatsApp (QR Mode)
```json
{
  "name": "WhatsApp Support",
  "type": "WhatsApp",
  "config": {
    "AuthMode": "qr",
    "DefaultAgentId": "gps-support-agent"
  }
}
```

### WhatsApp (Business API)
```json
{
  "name": "WhatsApp Business",
  "type": "WhatsApp",
  "config": {
    "AuthMode": "business",
    "ApiToken": "EAAB...",
    "PhoneNumberId": "123456789",
    "DefaultAgentId": "gps-support-agent"
  }
}
```

### Web Chat
```json
{
  "name": "Website Widget",
  "type": "WebChat",
  "config": {
    "DefaultAgentId": "support-agent",
    "WidgetTheme": "light"
  }
}
```

### API Direct
```json
{
  "name": "CRM Integration",
  "type": "Api",
  "config": {
    "ApiKey": "crm-api-key-123",
    "DefaultAgentId": "crm-assistant"
  }
}
```

## Implementation Guide

### Creating a New Channel Handler

1. Implement `IChannelHandler` interface:
```csharp
public class TelegramChannelHandler : IChannelHandler
{
    public ChannelType SupportedChannelType => ChannelType.Telegram;

    public Task<ChannelStatus> InitializeAsync(...) { ... }
    public Task ShutdownAsync(...) { ... }
    public Task<ChannelMessage?> ProcessIncomingMessageAsync(...) { ... }
    public Task<SendResult> SendReplyAsync(...) { ... }
    public ChannelContext ExtractContext(...) { ... }
    public Task<ChannelSession> GetOrCreateSessionAsync(...) { ... }
    public Task<HealthStatus> CheckHealthAsync(...) { ... }
}
```

2. Register in `DependencyInjection.cs`:
```csharp
services.AddSingleton<IChannelHandler, TelegramChannelHandler>();
```

3. Configure channel via API or UI

## Message Flow

1. **Incoming Message**
   - Channel receives message (webhook, WebSocket, API call)
   - ChannelHandler processes and normalizes to `ChannelMessage`
   - ChannelGateway routes to Agent Execution Engine
   - Agent processes and generates response
   - ChannelHandler sends reply through channel

2. **Session Management**
   - First message creates `ChannelSession`
   - Session links to `AgentId` and `ThreadId`
   - Session expires after configured TTL (default 24h)
   - Subsequent messages reuse existing session

3. **Context Propagation**
   - Channel-specific context (phone, userId, etc.)
   - User display name if available
   - Channel metadata (browser, IP, API version)
   - All available to agent for decision-making

## Security Considerations

- Channel credentials encrypted at rest
- Webhook signature verification (WhatsApp, Telegram)
- Rate limiting per channel/session
- Audit trail for all messages
- PII redaction in logs

## Monitoring

- Channel health checks
- Active session count
- Message throughput per channel
- Error rates by channel type
- Session duration analytics

## Future Enhancements

- [ ] Broadcast messaging to all sessions
- [ ] Channel-specific message templates
- [ ] Multi-channel session linking (same user, different channels)
- [ ] Channel analytics dashboard
- [ ] A/B testing across channels
- [ ] Channel-specific agent routing rules
