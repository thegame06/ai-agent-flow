# E2E example: third-party plugin publication

## 1) Build plugin package metadata
```json
{
  "extensionId": "vendor.example.crm-sync",
  "name": "CRM Sync",
  "version": "2.1.0",
  "vendor": "ExampleVendor",
  "description": "Synchronize contacts",
  "permissions": ["crm.read", "crm.write"],
  "riskLevel": "Medium",
  "compatibility": "agentflow-core>=1.0.0",
  "manifestJson": "{}",
  "payloadHash": "demo"
}
```

## 2) Sign payload
`signature = SHA256("extensionId|version|manifestJson|payloadHash")`

## 3) Register in catalog
```bash
curl -X POST http://localhost:5000/api/v1/extensions/catalog/register \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{
    "extensionId":"vendor.example.crm-sync",
    "name":"CRM Sync",
    "version":"2.1.0",
    "vendor":"ExampleVendor",
    "description":"Synchronize contacts",
    "permissions":["crm.read","crm.write"],
    "riskLevel":"Medium",
    "compatibility":"agentflow-core>=1.0.0",
    "source":"remote-marketplace",
    "signatureAlgorithm":"SHA256",
    "signature":"<hex-signature>",
    "manifestJson":"{}",
    "payloadHash":"demo"
  }'
```

## 4) Tenant install and enable
```bash
curl -X POST http://localhost:5000/api/v1/extensions/tenants/tenant-1/install \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"extensionId":"vendor.example.crm-sync","enableAfterInstall":true}'
```

## 5) Quarantine flow (if unsafe)
```bash
curl -X POST http://localhost:5000/api/v1/extensions/catalog/vendor.example.crm-sync/quarantine \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"reason":"suspicious outbound traffic"}'
```
