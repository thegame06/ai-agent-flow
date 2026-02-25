# Auth Profiles Integration (Unified AI Auth Wizard → AgentFlow)

## What was added

AgentFlow now includes a first-pass **Auth Profiles** capability inspired by `unified-ai-auth-wizard`:

- Tenant-scoped provider profiles (`provider`, `profileId`, `authType`, `secret`, `expiresAt`, `metadata`)
- CRUD-style management endpoints
- Basic profile health test endpoint
- Model routing linkage (`modelId -> providerProfileId`)

## API Endpoints

### Auth Profiles
Base: `/api/v1/tenants/{tenantId}/auth-profiles`

- `GET /` list profiles
- `POST /` create/update profile
- `POST /{profileId}/test` test profile state
- `DELETE /{profileId}` delete profile

### Model Routing linkage
Base: `/api/v1/model-routing`

- `POST /models` now accepts optional `providerProfileId`
- `POST /models/{modelId}/bind-profile` binds an existing profile to a model
- `GET /models` and `GET /providers/{providerId}/models` now include `providerProfileId`

## Security note

This is an MVP implementation. Secrets are encrypted for local persistence in process memory flows,
using an AES key derived from `AGENTFLOW_AUTH_KEY` (fallback dev key).

For production hardening, replace this with a proper secret manager/KMS or OS-native vault (Keychain/libsecret/DPAPI).
