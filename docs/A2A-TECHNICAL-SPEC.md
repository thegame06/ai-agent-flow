# Agents as Tools (A2A) — Technical Specification

**Feature ID**: A2A-001  
**Priority**: 🔥 CRITICAL (Unicorn Differentiator)  
**Status**: 🟢 IN DEVELOPMENT  
**Owner**: Engineering  
**Estimated Effort**: 2-3 weeks  

---

## 🎯 Executive Summary

**What**: Enable agents to invoke other agents as if they were tools.  
**Why**: Hierarchical composition is the killer feature that separates AgentFlow from "chatbot frameworks".  
**How**: Implement `AgentAsToolPlugin` that delegates to `IAgentExecutor` with circuit breaker protection.

### Business Value

- **Differentiation**: Only enterprise agent platform with deterministic hierarchical composition
- **Complexity Management**: Break complex tasks into specialized sub-agents
- **Reusability**: "Specialist" agents can be reused by multiple "Manager" agents
- **Marketplace Enablement**: Agents become publishable assets (not just tools)

---

## 🏗️ Architecture Overview

### Call Hierarchy Example

```
LoanOfficerAgent (Manager)
  ├─→ CreditCheckAgent (Specialist)
  │     └─→ BureauAPITool (Leaf)
  ├─→ RiskCalculatorAgent (Specialist)
  │     ├─→ FinancialModelTool (Leaf)
  │     └─→ HistoricalDataTool (Leaf)
  └─→ ApprovalAgent (Specialist with HITL)
        └─→ EmailNotificationTool (Leaf)
```

**Key Insight**: Each level has its own:
- Policy Engine evaluation
- Audit trail (WORM)
- Token budget
- Execution context

---

## 🔧 Component Design

### 1. AgentAsToolPlugin

**Location**: `src/AgentFlow.ToolSDK/BuiltIn/AgentAsToolPlugin.cs`

**Purpose**: IToolPlugin implementation that delegates to IAgentExecutor.

**Interface Contract**:
```csharp
public class AgentAsToolPlugin : IToolPlugin
{
    public ToolMetadata Metadata { get; }
    public ToolSchema GetSchema();
    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct);
}
```

**Input Schema**:
```json
{
  "type": "object",
  "properties": {
    "agentId": {
      "type": "string",
      "description": "ID of the agent to invoke"
    },
    "message": {
      "type": "string",
      "description": "Message to send to the delegated agent"
    },
    "variables": {
      "type": "object",
      "description": "Optional context variables to pass"
    }
  },
  "required": ["agentId", "message"]
}
```

**Output Schema**:
```json
{
  "type": "object",
  "properties": {
    "executionId": { "type": "string" },
    "finalResponse": { "type": "string" },
    "status": { "type": "string" },
    "steps": { "type": "integer" },
    "tokensUsed": { "type": "integer" }
  }
}
```

**Risk Level**: `Medium` (delegation has moderate risk)

---

### 2. CircuitBreakerService

**Location**: `src/AgentFlow.Core.Engine/CircuitBreakerService.cs`

**Purpose**: Prevent infinite recursion and runaway agent chains.

**Safety Limits**:
```csharp
public class CircuitBreakerConfig
{
    public int MaxCallDepth { get; set; } = 5;           // Max nesting levels
    public int MaxTotalExecutions { get; set; } = 50;    // Max agents in entire tree
    public int MaxTokenBudget { get; set; } = 100_000;   // Max tokens for entire chain
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(5);
}
```

**Enforcement Points**:
1. **Before delegation**: Check if depth limit exceeded
2. **After delegation**: Update parent execution with child stats
3. **On timeout**: Cancel entire chain (parent + all children)

**Example Error**:
```json
{
  "errorCode": "CircuitBreakerTripped",
  "message": "Max call depth (5) exceeded. Current depth: 6",
  "metadata": {
    "callChain": ["LoanOfficer", "CreditCheck", "BureauAPI", "RiskCalculator", "HistoryFetcher", "DatabaseQuery"],
    "parentExecutionId": "exec-123",
    "attemptedAgent": "DatabaseQuery"
  }
}
```

---

### 3. Enhanced Execution Context

**Modification**: Add `CallDepth` to track nesting level.

**Location**: `src/AgentFlow.Abstractions/Contracts.cs`

**Changes**:
```csharp
// IN: ToolExecutionContext
public sealed record ToolExecutionContext
{
    // ... existing properties ...
    public int CallDepth { get; init; } = 0;  // NEW: Nesting level (0 = root)
    public int TotalTokenBudget { get; init; } = 100_000;  // NEW: Total tokens allowed
    public int TokensUsedSoFar { get; init; } = 0;  // NEW: Tokens consumed in chain
}

// IN: AgentExecutionRequest
public sealed record AgentExecutionRequest
{
    // ... existing properties ...
    public int CallDepth { get; init; } = 0;  // NEW: Nesting level
    public int TokenBudget { get; init; } = 100_000;  // NEW: Max tokens for this chain
}
```

---

### 4. Token Budget Enforcement

**Location**: `src/AgentFlow.Core.Engine/TokenBudgetService.cs`

**Purpose**: Prevent cost explosion from recursive agent calls.

**Logic**:
```csharp
public class TokenBudgetService
{
    public bool CanProceed(int budgetRemaining, int estimatedCost)
    {
        // Safety margin: 10% buffer
        return (budgetRemaining * 0.9) >= estimatedCost;
    }
    
    public int CalculateRemainingBudget(
        int totalBudget, 
        int tokensUsedSoFar, 
        int childrenCount)
    {
        int remaining = totalBudget - tokensUsedSoFar;
        // Divide remaining budget equally among remaining children
        return remaining / Math.Max(childrenCount, 1);
    }
}
```

**Example Scenario**:
- Parent agent: 100,000 token budget
- After 2 tools: Used 5,000 tokens → Remaining: 95,000
- Delegates to 3 sub-agents → Each gets: 95,000 / 3 = 31,666 tokens
- If sub-agent exceeds 31,666 → Circuit breaker trips

---

## 📊 Data Model Changes

### AgentExecution Schema Enhancement

**MongoDB Collection**: `agent_executions`

**New Fields**:
```json
{
  "_id": "exec-456",
  "parentExecutionId": "exec-123",  // EXISTING (already supported!)
  "callDepth": 2,                    // NEW
  "callChain": [                     // NEW: Full ancestor chain
    "exec-123",
    "exec-234", 
    "exec-456"
  ],
  "tokensUsed": 15000,               // EXISTING
  "tokenBudget": 31666,              // NEW
  "childExecutions": [               // NEW: Trace children
    {
      "executionId": "exec-789",
      "agentKey": "risk-calculator",
      "status": "Completed",
      "tokensUsed": 8000
    }
  ]
}
```

---

## 🔒 Security & Governance

### Policy Enforcement at Each Level

**Scenario**: LoanOfficerAgent delegates to CreditCheckAgent

**Policy Checkpoints**:
1. **Parent (LoanOfficer)**:
   - PreAgent: Verify user authorized to process loans
   - PreTool: Verify allowed to delegate to CreditCheckAgent
   - PostTool: Inspect CreditCheckAgent result for PII leaks

2. **Child (CreditCheck)**:
   - PreAgent: Verify CreditCheckAgent is allowed for this tenant
   - PreTool: Verify allowed to call BureauAPI
   - PostTool: Sanitize SSN/PII before returning to parent

**Key Principle**: **Each agent runs in its own policy context.** Child cannot bypass parent policies.

---

### Audit Trail (WORM)

**Requirement**: Every delegation must be immutably logged.

**Example Audit Entry**:
```json
{
  "timestamp": "2026-02-21T15:30:00Z",
  "eventType": "AgentDelegation",
  "parentExecutionId": "exec-123",
  "childExecutionId": "exec-456",
  "delegatedAgent": "credit-check-agent",
  "delegatedBy": "loan-officer-agent",
  "reason": "LLM decided to verify credit history",
  "llmRationale": "User requested loan approval, need credit score",
  "inputParameters": {
    "userId": "user-789",
    "loanAmount": 50000
  },
  "policyDecision": "Allowed",
  "callDepth": 2,
  "tokensAllocated": 31666
}
```

**Storage**: MongoDB `agent_execution_events` collection (append-only).

---

## 🧪 Testing Strategy

### Unit Tests

**File**: `tests/AgentFlow.Tests.Unit/ToolSDK/AgentAsToolPluginTests.cs`

**Scenarios**:
1. ✅ **Happy path**: Manager delegates to specialist, specialist completes successfully
2. ✅ **Circuit breaker - depth**: Delegation fails when max depth (5) exceeded
3. ✅ **Circuit breaker - tokens**: Delegation fails when token budget exhausted
4. ✅ **Circuit breaker - timeout**: Delegation fails when timeout exceeded
5. ✅ **Policy violation**: Child agent violates policy → Parent receives error
6. ✅ **Child failure**: Child agent fails → Parent receives error result (not exception)
7. ✅ **Circular reference**: Agent A → Agent B → Agent A (detected and blocked)

---

### Integration Tests

**File**: `tests/AgentFlow.Tests.Integration/A2A/HierarchicalAgentTests.cs`

**Scenarios**:
1. **3-Level Hierarchy**: LoanOfficer → CreditCheck → BureauAPI
2. **Parallel Delegation**: Manager delegates to 3 specialists in sequence
3. **Token Budget Distribution**: Verify child agents get fair share of tokens
4. **Audit Trail Completeness**: Verify every delegation logged to MongoDB
5. **OpenTelemetry Traces**: Verify parent-child span relationships

---

### End-to-End Tests

**Demo Scenario**: Loan Officer Agent

**Setup**:
1. Create `LoanOfficerAgent` (Manager)
2. Create `CreditCheckAgent` (Specialist)
3. Create `RiskCalculatorAgent` (Specialist)
4. Create `ApprovalAgent` (Specialist with HITL)

**Execution Flow**:
```
User: "Approve loan for John Doe, $50,000"
  
LoanOfficerAgent (Think):
  → "I need to check credit first"
  → Delegates to CreditCheckAgent("John Doe")

CreditCheckAgent (Think):
  → "I'll call the bureau API"
  → Calls BureauAPITool("John Doe")
  → Returns: { "score": 720, "history": "good" }

LoanOfficerAgent (Observe credit result):
  → "Score 720 is good, now calculate risk"
  → Delegates to RiskCalculatorAgent({ score: 720, amount: 50000 })

RiskCalculatorAgent (Think):
  → Calls FinancialModelTool({ score: 720, amount: 50000 })
  → Returns: { "risk": "Low", "recommendation": "Approve" }

LoanOfficerAgent (Observe risk result):
  → "Risk is Low, proceed to approval"
  → Delegates to ApprovalAgent({ recommendation: "Approve" })

ApprovalAgent (HITL enabled):
  → Checkpoint: "Human review required for approval"
  → Status: HumanReviewPending

Final Result: Execution paused, awaiting human approval
```

**Verification**:
- ✅ 5 agent executions in database (1 parent + 4 children)
- ✅ Call depth correctly tracked (0 → 1 → 2)
- ✅ Total tokens < budget (100,000)
- ✅ Audit trail has 4 delegation events
- ✅ OpenTelemetry shows 4-level trace hierarchy

---

## 🚀 Implementation Roadmap

### Week 1: Core Implementation

**Days 1-2**:
- ✅ Create `AgentAsToolPlugin.cs` (bare bones)
- ✅ Add `CallDepth` to `ToolExecutionContext` and `AgentExecutionRequest`
- ✅ Create `CircuitBreakerService.cs` with max depth enforcement
- ✅ Modify `AgentExecutionEngine` to populate `CallDepth`

**Days 3-4**:
- ✅ Create `TokenBudgetService.cs`
- ✅ Implement token budget enforcement in `AgentAsToolPlugin`
- ✅ Add `childExecutions` tracking to `AgentExecution` aggregate
- ✅ Unit tests for CircuitBreaker + TokenBudget

**Day 5**:
- ✅ Integration test: 3-level hierarchy
- ✅ OpenTelemetry span hierarchy verification
- ✅ Code review + documentation

---

### Week 2: Governance & Safety

**Days 6-7**:
- ✅ Policy enforcement at each delegation level
- ✅ Circular reference detection (Agent A → B → A)
- ✅ Enhanced audit logging for delegations
- ✅ Error handling (child failures don't crash parent)

**Days 8-9**:
- ✅ MongoDB schema updates (`callDepth`, `callChain`, `childExecutions`)
- ✅ Repository methods for querying hierarchies
- ✅ Performance testing (1000+ delegations)

**Day 10**:
- ✅ Documentation updates:
  - [TOOL-SDK-ARCHITECTURE.md](TOOL-SDK-ARCHITECTURE.md) — Add A2A section
  - [architecture.md](architecture.md) — Add hierarchical composition diagram
- ✅ API documentation (Swagger)

---

### Week 3: Demo & Polish

**Days 11-13**:
- ✅ Implement Loan Officer demo agents:
  - `LoanOfficerAgent.json` (DSL)
  - `CreditCheckAgent.json` (DSL)
  - `RiskCalculatorAgent.json` (DSL)
  - `ApprovalAgent.json` (DSL with HITL)
- ✅ Reference plugins:
  - `BureauAPIPlugin` (mock)
  - `FinancialModelPlugin` (mock)
  - `EmailNotificationPlugin` (real)

**Day 14**:
- ✅ Video recording (3 minutes):
  - 0:00-0:30: Problem statement
  - 0:30-1:30: Architecture explanation (diagram)
  - 1:30-2:30: Live demo (Loan Officer flow)
  - 2:30-3:00: Call to action
- ✅ Upload to YouTube, LinkedIn, AgentFlow website

**Day 15**:
- ✅ Update pitch deck with A2A slide
- ✅ Blog post: "How AgentFlow Enables Hierarchical Agent Composition"
- ✅ Press release prep for investor outreach

---

## 📈 Success Metrics

### Technical KPIs

| Metric | Target | Measurement |
|---|---|---|
| **Max Call Depth Supported** | 10 levels | Integration test |
| **Token Budget Accuracy** | ±5% | Unit test |
| **Circuit Breaker Response Time** | <10ms | Benchmark |
| **Audit Trail Completeness** | 100% | Integration test |
| **OpenTelemetry Overhead** | <5% latency | Performance test |

### Business KPIs

| Metric | Target | Timeline |
|---|---|---|
| **Demo Video Views** | 1,000 | 2 weeks |
| **NuGet Downloads** | 100 | 1 month |
| **Investor Demo Bookings** | 5 | 2 weeks |
| **GitHub Stars (ToolSDK)** | 50 | 1 month |

---

## 🚨 Risk Mitigation

### Risk 1: Infinite Recursion

**Mitigation**:
- ✅ CircuitBreaker enforces max depth (default: 5)
- ✅ Circular reference detection (track call chain)
- ✅ Timeout enforcement (max 5 minutes per chain)

---

### Risk 2: Cost Explosion

**Mitigation**:
- ✅ Token budget enforcement (default: 100k per chain)
- ✅ Alert when 80% budget consumed
- ✅ Hard stop at 100% budget
- ✅ Per-tenant budget limits in database

---

### Risk 3: Complex Debugging

**Mitigation**:
- ✅ OpenTelemetry hierarchical traces (Jaeger UI)
- ✅ Call chain visualization in Designer UI
- ✅ Step-by-step execution replay
- ✅ LLM rationale logged at every delegation

---

## 🎯 Competitive Differentiation

### AgentFlow A2A vs. Competitors

| Feature | LangChain | AutoGen | Semantic Kernel | **AgentFlow A2A** |
|---|---|---|---|---|
| **Hierarchical Composition** | ❌ No | 🟡 Uncontrolled | ❌ No | ✅ **Deterministic** |
| **Circuit Breaker** | ❌ No | ❌ No | ❌ No | ✅ **Built-in** |
| **Token Budget per Chain** | ❌ No | ❌ No | ❌ No | ✅ **Enforced** |
| **Audit Trail (WORM)** | ❌ No | ❌ No | ❌ No | ✅ **Immutable** |
| **Policy at Each Level** | ❌ No | ❌ No | ❌ No | ✅ **Recursive** |
| **OpenTelemetry Traces** | 🟡 Partial | ❌ No | ❌ No | ✅ **Full hierarchy** |

**Conclusion**: AgentFlow is the **only** platform with enterprise-grade hierarchical agent composition.

---

## 📚 References

- [UNICORN-STRATEGY.md](UNICORN-STRATEGY.md) — Strategic vision
- [TOOL-SDK-ARCHITECTURE.md](TOOL-SDK-ARCHITECTURE.md) — Plugin SDK design
- [architecture.md](architecture.md) — Core architecture
- [ESTADO-PROYECTO-UNICORNIO.md](ESTADO-PROYECTO-UNICORNIO.md) — Project status

---

**Last Updated**: February 21, 2026  
**Next Review**: February 28, 2026 (post-implementation)  
**Status**: 🟢 Ready for Implementation

---

## 🚀 Quick Start for Developers

### Create a Manager Agent

```json
// loan-officer-agent.json (DSL)
{
  "name": "Loan Officer Agent",
  "description": "Processes loan applications",
  "tools": [
    {
      "id": "agent-as-tool",
      "name": "delegate-to-agent"
    }
  ],
  "systemPrompt": "You are a loan officer. Delegate to specialists: CreditCheckAgent for credit, RiskCalculatorAgent for risk."
}
```

### Execute from Code

```csharp
var request = new AgentExecutionRequest
{
    TenantId = "tenant-acme-bank",
    AgentKey = "loan-officer",
    UserId = "user-789",
    UserMessage = "Approve loan for John Doe, $50,000",
    CallDepth = 0, // Root level
    TokenBudget = 100_000
};

var result = await _executor.ExecuteAsync(request);

// Result includes child execution details
Console.WriteLine($"Total steps: {result.TotalSteps}");
Console.WriteLine($"Tokens used: {result.TotalTokensUsed}");
Console.WriteLine($"Child executions: {result.RuntimeSnapshot.ChildExecutions.Count}");
```

---

**Ready to build the future? Let's ship A2A. 🚀**
