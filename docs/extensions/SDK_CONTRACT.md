# AgentFlow Marketplace SDK Contract (v1)

## Package manifest
Third-party publishers must provide:

- `extensionId` (stable identifier)
- `name`
- `version` (semver)
- `vendor`
- `description`
- `permissions` (least privilege)
- `riskLevel` (`Low|Medium|High|Critical`)
- `compatibility` (e.g. `agentflow-core>=1.0.0`)
- `manifestJson`
- `payloadHash`
- `signatureAlgorithm` (`SHA256`)
- `signature` (hex SHA-256 of `extensionId|version|manifestJson|payloadHash`)

## Publish endpoint
`POST /api/v1/extensions/catalog/register`

## Tenant lifecycle endpoints
- Install: `POST /api/v1/extensions/tenants/{tenantId}/install`
- Uninstall: `POST /api/v1/extensions/tenants/{tenantId}/uninstall`
- Enable: `POST /api/v1/extensions/tenants/{tenantId}/enable`
- Disable: `POST /api/v1/extensions/tenants/{tenantId}/disable`
- Allowlist: `PUT /api/v1/extensions/tenants/{tenantId}/allowlist`

## Security controls
- package signature verification (SHA-256)
- tenant allowlist enforcement
- minimum required permissions in manifest
- quarantined plugin auto-disable
- health visibility via `/api/v1/extensions/catalog`
