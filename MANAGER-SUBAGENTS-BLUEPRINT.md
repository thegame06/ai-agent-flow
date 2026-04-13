# Manager + Sub‑Agents Blueprint (Intent Routing)

## Goal
One **Manager Agent** exposed to channels (WhatsApp/web) that routes each request to one or more **Sub‑agents** specialized by intent.

---

## Mental Model

- **Manager Agent**
  - Owns channel/session
  - Detects intent and context
  - Decides handoff target(s)
  - Keeps conversation continuity (sticky routing)

- **Sub‑agents**
  - No direct channel required
  - Execute domain tasks (Sales, Collections, Reservations, Billing)
  - Can use N tools via MCP/custom extensions

- **MCP layer (preferred)**
  - Drive, Sheets, Jira, Confluence, HubSpot, etc.
  - Standardized server/tool onboarding
  - Custom Extensions only when no MCP integration exists

---

## Routing Contract (DoD)

1. Manager classifies intent (`sales`, `billing`, `support`, `collections`, etc.).
2. Manager resolves target sub‑agent by policy + allowlist.
3. If customer has active sub‑agent session, keep sticky until closure.
4. Sub‑agent returns structured outcome (`result`, `confidence`, `next_action`).
5. Manager decides: reply, escalate, or chain another sub‑agent.

---

## Suggested Sub‑agents

- `sales-agent`
  - quote/availability/discount logic
  - Sheets + Drive MCP
- `collections-agent`
  - scheduled reminders and payment follow-up
  - Sheets + WhatsApp send + Drive upload
- `reservation-agent`
  - reservation state machine + confirmation docs
- `ops-agent`
  - Jira/Confluence updates

---

## Use Case A: Car rental (inbound)

1. Customer asks in WhatsApp
2. Manager detects intent (quote/reserve/pay)
3. Handoff to `sales-agent` or `reservation-agent`
4. Sub‑agent reads Sheets availability/pricing
5. On payment proof: upload file to Drive + update sheet row
6. Manager responds final status to customer

---

## Use Case B: Daily collections (outbound)

1. Scheduler triggers 09:00 daily
2. `collections-agent` searches file `yyyy_mm_dd_collection`
3. Parses rows and sends reminders per customer
4. Updates result status per row
5. Produces run summary (sent/failed/retry)

---

## Channel Multiplexing Strategy

- One channel can serve many domains through one manager.
- Manager owns routing lock:
  - `session.owner = sub-agent-id`
  - `session.intent = ...`
  - `session.state = active|waiting|closed`
- Prevents cross-bot contamination and context loss.

### Session Ownership Lifecycle (normative)

To make ownership deterministic under concurrency, treat `session.owner`, `session.intent`, and `session.state` as a single atomic envelope.

#### Valid values

- `session.owner`
  - `manager` (no delegated specialist currently holding the lock)
  - `<sub-agent-id>` (specialist currently owning execution rights)
- `session.intent`
  - `unclassified` (before first intent classification)
  - `<intent-id>` (`sales`, `billing`, `support`, `collections`, etc.)
  - `handoff:<intent-id>` (transient value while handoff is in-flight)
- `session.state`
  - `active` (owner can execute)
  - `waiting` (blocked on external input/tool/customer)
  - `handoff_pending` (manager selected target; ownership transfer not committed yet)
  - `handoff_committed` (target accepted handoff and owns lock)
  - `recovering` (safe replay/compensation in progress)
  - `closed` (terminal, no new execution without explicit reopen)

#### Allowed transitions

1. Session open:
   - `(owner=manager, intent=unclassified, state=active)`
2. Intent routed (sticky manager):
   - `active -> handoff_pending` with `intent=handoff:<intent-id>`
3. Handoff accepted:
   - `handoff_pending -> handoff_committed -> active`
   - final envelope: `(owner=<sub-agent-id>, intent=<intent-id>, state=active)`
4. External wait:
   - `active -> waiting` (same owner + intent)
5. Resume after wait:
   - `waiting -> active` (same owner + intent)
6. Failure recovery:
   - `active|waiting -> recovering -> active|closed`
7. Explicit closure:
   - `active|waiting|recovering -> closed`, owner reset to `manager`

#### Invalid transitions (must reject)

- `closed -> active` without explicit `reopen_session` command.
- Direct `active(manager) -> active(other-sub-agent)` without `handoff_pending`.
- Any transition changing both owner and intent without matching handoff event.

---

## Lock TTL, Heartbeat, and Safe Takeover

### Lock model

- Lock key: `session:<tenant_id>:<channel_id>:<session_id>:owner_lock`
- Lock payload (JSON):
  - `owner`, `intent`, `state`, `lock_version`, `expires_at`, `correlation_id`
- Acquisition must be **compare-and-set (CAS)** on `lock_version`.

### Recommended timing

- `lock_ttl = 45s`
- `heartbeat_interval = 15s` (≈ TTL/3)
- `heartbeat_grace = 10s` (network jitter / short pauses)
- effective expiry threshold: `now > expires_at + heartbeat_grace`

### Heartbeat contract

- Current owner sends heartbeat with:
  - same `owner`
  - monotonic `lock_version + 1`
  - `expires_at = now + lock_ttl`
- Heartbeat failure threshold: 2 consecutive failures => transition to `recovering`.

### Safe takeover on expiration

1. Candidate (normally `manager`) reads expired lock.
2. Candidate writes `takeover_pending` event with new `correlation_id`.
3. Candidate performs CAS update:
   - expected: old `lock_version`
   - new: `owner=manager`, `state=recovering`, `lock_version+1`
4. If CAS fails, abort takeover (another owner already won race).
5. Manager runs recovery policy:
   - replay from last checkpoint OR compensation for partially executed side effects.
6. Manager sets final state:
   - `active` (recovered) or `closed` (irrecoverable).

---

## Handoff Contract with `correlation_id` + `idempotency_key`

Every handoff request must carry:

- `correlation_id`: same value across the full business flow (trace-level identity).
- `idempotency_key`: unique per execution attempt semantics (dedupe identity).

### Key format (recommended)

- `correlation_id = <channel_message_id|thread_id>`
- `idempotency_key = sha256(tenant_id + session_id + source_agent + target_agent + intent + normalized_payload_hash)`

### Processing rules

1. On handoff receive, target checks idempotency store:
   - if key exists with `status=completed`, return cached result.
   - if key exists with `status=in_progress`, return `202 accepted` + polling token.
   - if missing, insert `in_progress` atomically and continue.
2. On completion, persist terminal status (`completed` / `failed_compensated`) + response digest.
3. Retention:
   - idempotency records TTL >= `max(flow_ttl, retry_window)` (recommended 24h).

This prevents duplicate execution caused by retries, webhook redelivery, and race conditions between manager replicas.

---

## Retry and Compensation Policy (sub-agent failure mid-flow)

Use step-level execution metadata:

- `step_id`, `side_effect_type`, `is_idempotent`, `compensation_action`, `max_retries`.

### Retry policy matrix

- Transient errors (`timeout`, `429`, `5xx`, transport):
  - retries: 3
  - backoff: exponential with jitter (e.g., 1s, 2s, 4s ± 30%)
- Concurrency conflicts (CAS/version mismatch):
  - retries: 2
  - immediate small jitter (50-150ms)
- Deterministic business errors (validation/policy denied):
  - retries: 0
  - route to compensation/escalation

### Compensation policy

- For each non-idempotent committed side effect, define compensator:
  - payment capture -> refund/void
  - ticket creation -> close/cancel with reason
  - outbound message -> append corrective follow-up record (never “delete sent”)
- If compensator fails:
  - mark `failed_compensation`
  - escalate to human queue with full `correlation_id` trace.

### Recovery checkpointing

- Persist checkpoint after each step commit:
  - `last_completed_step`
  - `side_effect_journal[]`
  - `compensation_status`
- Restart resumes from checkpoint, never from flow start, to avoid duplicate side effects.

---

## Verifiable “Zero Collisions” Acceptance Criteria

“Zero collisions” means no session executes conflicting owners or duplicate side effects for the same idempotency domain.

### Hard invariants

1. At most one active owner lock per session/version.
2. No two successful non-read operations share same `idempotency_key`.
3. No state transition violates allowed graph above.
4. Every side effect has exactly one terminal record: `committed` or `compensated`.

### Concurrency test cases (must pass)

1. **Dual manager race**:
   - 2 manager instances try same handoff in <10ms.
   - expected: 1 CAS success, 1 CAS reject, 0 duplicates.
2. **Duplicate webhook delivery**:
   - same inbound payload delivered 3 times.
   - expected: single sub-agent execution, cached responses for duplicates.
3. **Owner heartbeat loss**:
   - drop heartbeats for > TTL + grace.
   - expected: takeover by manager, no double side effect.
4. **Late heartbeat after takeover**:
   - previous owner heartbeat arrives after manager takeover.
   - expected: rejected by stale `lock_version`.

### Controlled chaos scenarios (must pass)

- Inject 20% random timeout to downstream tools for 30 minutes.
- Randomly kill 1 of N worker pods every 2 minutes.
- Introduce ±5s clock skew on one node.

Expected outcomes:

- collision rate = `0`
- duplicate side effects = `0`
- all failed flows end in `failed_compensation` or human escalation with traceability.

---

## Product UX Simplification (recommended)

1. **Wizard**: Create Manager + pick sub‑agents
2. **Wizard**: Connect MCP servers + expose tools
3. **Wizard**: Attach channel + test 5 sample intents
4. **Monitor**: Sessions by owner sub‑agent + handoff trace

---

## Current Status in UI

- Threads page fixed to backend contract mapping
- Feature Flags page added
- Segment Routing page added
- Channels default agent selector enabled

Next high-value UI step: "Manager Orchestration" view showing
- intent decision
- selected sub‑agent
- reason/policy
- session ownership timeline
