# Release Readiness Checklist

## 1. Scope and Change Control
- [ ] Confirm release scope (features, bugfixes, migrations, infra changes).
- [ ] Confirm no out-of-scope staged files are included.
- [ ] Freeze branch for release candidate (no direct pushes without review).

## 2. Backend Quality Gates
- [ ] Build passes:
  - `dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj -v minimal`
- [ ] Unit tests pass:
  - `dotnet test tests/AgentFlow.Tests.Unit/AgentFlow.Tests.Unit.csproj -v minimal`
- [ ] No new critical warnings introduced by this release.

## 3. Frontend Quality Gates
- [ ] Lint passes:
  - `cd frontend/aiagent_flow && npm run lint`
- [ ] Production build passes:
  - `cd frontend/aiagent_flow && npm run build`
- [ ] Core routes smoke-tested manually:
  - `/dashboard/overview`
  - `/dashboard/operations`
  - `/dashboard/channels`
  - `/dashboard/workflows`

## 4. Local Stack and Ports
- [ ] Verify required local ports:
  - `make check-local-ports`
- [ ] Verify stack health:
  - `make verify-local-stack`
- [ ] Ensure docker services healthy (`mongo`, `redis`, `nats`, etc.).

## 5. Event Backbone and DLQ Operations
- [ ] Event transport configured as expected per environment (`InProcess` or `Nats`).
- [ ] DLQ store configured (`Redis` in local distributed mode).
- [ ] Deadletter list endpoint works:
  - `make deadletters-list TENANT_ID=tenant-1`
- [ ] Replay path works on test deadletter:
  - `make deadletters-replay TENANT_ID=tenant-1 DEADLETTER_ID=<id>`

## 6. Voice and Runtime Validation
- [ ] Voice inbound event path validated (`connect.call.received` -> runtime dispatch).
- [ ] STT/TTS provider-chain fallback verified on forced provider failure.
- [ ] Playback path verified (`connect.call.audio.synthesized` -> call control update).
- [ ] Session behavior verified:
  - Turn progression in voice session
  - Session cleanup on `connect.call.ended`

## 7. Observability and Audit
- [ ] Telemetry exporter mode confirmed for environment:
  - `Telemetry:Exporter = none|console`
- [ ] Audit summary endpoint returns expected signals and counters.
- [ ] Operational alerts visible in Operations UI.

## 8. Security and Access
- [ ] Confirm tenant isolation on operational endpoints.
- [ ] Confirm no plaintext secrets in appsettings or logs.
- [ ] Confirm protected routes require auth and expected role permissions.

## 9. Rollback Plan
- [ ] Previous stable artifact/tag identified.
- [ ] Rollback command/process documented for API and frontend.
- [ ] Data-impact review complete (no irreversible migration in this release).

## 10. Go/No-Go
- [ ] Release owner sign-off.
- [ ] QA sign-off.
- [ ] Ops sign-off.
- [ ] Final go/no-go decision recorded with timestamp.

