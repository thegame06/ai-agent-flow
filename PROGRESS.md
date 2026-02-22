# AgentFlow — Project Progress & Changelog

## [2026-02-21] — Phase 2/3: Core Logic & Enterprise Governance

### 🚀 New Features & Enhancements

#### 1. Human-in-the-Loop (HITL) system
- **State Machine Integration**: Added `PausedForReview` state to `AgentExecution`.
- **Checkpoint Store**: Implemented `MongoCheckpointStore` for persistent storage of pending reviews.
- **Contextual Review**: Checkpoints now capture LLM rationale, tool name, and proposed inputs.
- **Resume Capability**: Added `/api/v1/tenants/{tenantId}/checkpoints/{executionId}/decide` endpoint to allow manual approval or rejection.

#### 2. Segment-Based Policy Rules
- **Granular Control**: Policies can now be targeted to specific user segments (e.g., `VIP`, `Standard`, `Trial`).
- **Context Awareness**: `PolicyEvaluationContext` now includes `UserSegments`, derived from JWT claims.
- **Automatic Matching**: The `CompositePolicyEngine` automatically filters rules based on the user's segments.

#### 3. Shadow Evaluation (A/B Testing)
- **Champion/Challenger Model**: Added `ShadowAgentId` to `AgentDefinition`.
- **Parallel Trace**: The `EvaluationBackgroundWorker` now triggers shadow executions when a "Champion" agent completes.
- **Risk-Free Deployment**: Allows comparing performance scores between an active agent and a candidate version without affecting real production output.

#### 4. Persistent Prompt Engine
- **Profile Store**: Implemented `MongoPromptProfileStore` for versioned prompt templates.
- **Dynamic Rendering**: Integrated `PromptRenderer` into the execution loop, ensuring prompts are composed from persistent profiles.

#### 5. Frontend — Command Center (MVP)
- **Overview Dashboard**: Added "Command Center" with real-time metrics (Agent count, Executions, Quality Score).
- **Review Queue**: Created a dedicated HITL interface for approving/rejecting paused executions.
- **Decision Trace Detail**: Implemented a rich timeline view for execution history, showing every "Think-Plan-Act-Observe" step with LLM reasoning and data payloads.

### 🔧 Architecture Updates
- Updated `AgentFlow.Abstractions` with new contracts: `ICheckpointStore`, `IPromptProfileStore`, and enhanced `PolicyDefinition`.
- Registered new services in `AgentFlow.Api` and `AgentFlow.Worker`.
- Updated `docs/architecture.md` and `docs/mongodb-data-model.md`.

### ✅ Build Status
- **Backend**: `dotnet build AgentFlow.sln` — Passing.
- **Frontend**: `npm run build` (aiagent_flow) — Passing (after lint fixes).
