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
