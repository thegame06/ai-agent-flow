# AgentFlow — MongoDB Data Model

## Collections Overview

All collections use **compound indices with TenantId as the leading key**.
This ensures MongoDB query planner always uses the tenant index first.

---

## `agent_definitions` Collection

```json
{
  "_id": "ObjectId",
  "tenantId": "674abc123...",
  "name": "Customer Support Agent",
  "description": "Handles tier-1 customer queries",
  "status": "Published",
  "version": 3,
  "isDeleted": false,
  "createdAt": "2026-02-19T21:00:00Z",
  "updatedAt": "2026-02-19T22:00:00Z",
  "createdBy": "user_abc",
  "updatedBy": "user_xyz",
  "brain": {
    "modelId": "gpt-4o",
    "provider": "OpenAI",
    "systemPromptTemplate": "You are a helpful customer support agent for {{company}}...",
    "temperature": 0.3,
    "topP": 1.0,
    "maxResponseTokens": 2048,
    "requiresToolExecution": true,
    "isSystemPromptValidated": true
  },
  "loopConfig": {
    "maxIterations": 10,
    "maxExecutionTimeSeconds": 300,
    "toolCallTimeoutSeconds": 30,
    "maxRetries": 3,
    "retryBackoffBaseSeconds": 2,
    "allowParallelToolCalls": false,
    "enableHumanInTheLoop": false,
    "plannerType": "Sequential"
  },
  "authorizedTools": [
    {
      "toolId": "tool_search_kb",
      "toolName": "SearchKnowledgeBase",
      "toolVersion": "2.1.0",
      "grantedPermissions": ["read"],
      "isEnabled": true,
      "maxCallsPerExecution": 5
    },
    {
      "toolId": "tool_create_ticket",
      "toolName": "CreateSupportTicket",
      "toolVersion": "1.0.0",
      "grantedPermissions": ["write"],
      "isEnabled": true,
      "maxCallsPerExecution": 1
    }
  ],
  "memory": {
    "enableWorkingMemory": true,
    "workingMemoryTtlSeconds": 3600,
    "enableLongTermMemory": true,
    "enableVectorMemory": true,
    "vectorCollectionName": "customer_support_kb",
    "vectorSearchTopK": 5,
    "vectorMinRelevanceScore": 0.75
  },
  "shadowAgentId": "674def111...",
  "ownerUserId": "user_abc",
  "allowedRoles": ["operator", "developer"],
  "tags": ["customer-support", "tier-1", "production"]
}
```

### Indices
```javascript
db.agent_definitions.createIndex({ tenantId: 1, status: 1, createdAt: -1 })
db.agent_definitions.createIndex(
  { tenantId: 1, name: 1 }, 
  { unique: true, partialFilterExpression: { isDeleted: false } }
)
```

---

## `agent_executions` Collection

```json
{
  "_id": "ObjectId",
  "tenantId": "674abc123...",
  "agentDefinitionId": "674def456...",
  "triggeredBy": "user_xyz",
  "status": "Completed",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "version": 5,
  "createdAt": "2026-02-19T21:05:00Z",
  "updatedAt": "2026-02-19T21:05:45Z",
  "startedAt": "2026-02-19T21:05:01Z",
  "completedAt": "2026-02-19T21:05:44Z",
  "currentIteration": 3,
  "maxIterations": 10,
  "priority": "Normal",
  "input": {
    "userMessage": "I need to return my order #12345",
    "contextJson": "{\"customerId\": \"cust_99\"}",
    "variables": { "orderId": "12345" },
    "language": "en"
  },
  "output": {
    "finalResponse": "I've created ticket #TKT-9876 for your return request...",
    "structuredOutputJson": null,
    "totalTokensUsed": 1847,
    "totalToolCalls": 2,
    "totalIterations": 3
  },
  "steps": [
    {
      "id": "ObjectId",
      "stepType": "Think",
      "iteration": 1,
      "startedAt": "2026-02-19T21:05:02Z",
      "completedAt": "2026-02-19T21:05:05Z",
      "durationMs": 3120,
      "llmResponse": "{\"decision\":\"UseTool\",\"rationale\":\"Need to search KB first\",\"nextToolName\":\"SearchKnowledgeBase\",...}",
      "tokensUsed": 523,
      "thinkingRationale": "User wants to return order. Should search KB for return policy first.",
      "isSuccess": true
    },
    {
      "id": "ObjectId",
      "stepType": "Act",
      "iteration": 1,
      "startedAt": "2026-02-19T21:05:05Z",
      "completedAt": "2026-02-19T21:05:08Z",
      "durationMs": 2890,
      "toolId": "tool_search_kb",
      "toolName": "SearchKnowledgeBase",
      "inputJson": "{\"query\": \"return policy\", \"topK\": 3}",
      "outputJson": "{\"results\": [{\"content\": \"Returns accepted within 30 days...\"}]}",
      "isSuccess": true
    },
    {
      "id": "ObjectId",
      "stepType": "Observe",
      "iteration": 1,
      "startedAt": "2026-02-19T21:05:08Z",
      "completedAt": "2026-02-19T21:05:11Z",
      "durationMs": 2340,
      "llmResponse": "{\"summary\":\"Policy found. Returns accepted within 30 days.\",\"goalAchieved\":false}",
      "tokensUsed": 312,
      "isSuccess": true
    }
  ],
  "errorMessage": null,
  "errorCode": null,
  "retryCount": 0
}
```

### Indices
```javascript
db.agent_executions.createIndex({ tenantId: 1, createdAt: -1 })
db.agent_executions.createIndex({ tenantId: 1, status: 1, createdAt: -1 })
db.agent_executions.createIndex({ tenantId: 1, agentDefinitionId: 1, createdAt: -1 })
db.agent_executions.createIndex({ correlationId: 1 })
// TTL index for execution cleanup (optional, 90 day retention)
db.agent_executions.createIndex({ createdAt: 1 }, { expireAfterSeconds: 7776000 })
```

---

## `agent_checkpoints` Collection (Human-in-the-Loop)

```json
{
  "_id": "ObjectId",
  "tenantId": "674abc123...",
  "executionId": "674def456...",
  "checkpointId": "step_2_validation",
  "status": "Pending",
  "reason": "High risk tool execution: CreateSupportTicket",
  "toolName": "CreateSupportTicket",
  "toolInputJson": "{\"title\":\"Battery issue\"}",
  "llmRationale": "User requested a support ticket for their hardware problem.",
  "createdAt": "2026-02-19T21:05:30Z",
  "decidedAt": null,
  "decidedBy": null,
  "decision": null,
  "feedback": null
}
```

### Indices
```javascript
db.agent_checkpoints.createIndex({ tenantId: 1, status: 1, createdAt: -1 })
db.agent_checkpoints.createIndex({ executionId: 1 }, { unique: true })
```

---

## `tool_definitions` Collection

```json
{
  "_id": "ObjectId",
  "tenantId": "platform",
  "name": "SearchKnowledgeBase",
  "description": "Searches the tenant knowledge base using semantic similarity",
  "version": "2.1.0",
  "scope": "Platform",
  "status": "Active",
  "riskLevel": "Low",
  "requiresSandbox": false,
  "requiresExplicitApproval": false,
  "requiredPermissions": ["tool:execute:low"],
  "category": "Knowledge",
  "implementationType": "AgentFlow.Tools.SearchKnowledgeBaseTool",
  "inputSchemaJson": "{\"type\":\"object\",\"required\":[\"query\"],\"properties\":{\"query\":{\"type\":\"string\",\"maxLength\":500},\"topK\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}}}",
  "outputSchemaJson": "{\"type\":\"object\",\"properties\":{\"results\":{\"type\":\"array\"}}}",
  "createdAt": "2026-01-01T00:00:00Z",
  "updatedAt": "2026-02-01T00:00:00Z",
  "createdBy": "platform_admin",
  "isDeleted": false,
  "version": 1
}
```

---

## `tool_execution_logs` Collection (WORM - Append Only)

```json
{
  "_id": "ObjectId",
  "tenantId": "674abc123...",
  "executionId": "674ghi789...",
  "stepId": "674jkl012...",
  "toolId": "tool_search_kb",
  "toolName": "SearchKnowledgeBase",
  "toolVersion": "2.1.0",
  "agentId": "674def456...",
  "userId": "user_xyz",
  "inputJson": "{\"query\":\"return policy\",\"topK\":3}",
  "outputJson": "{\"results\":[...]}",
  "errorMessage": null,
  "isSuccess": true,
  "durationMs": 2890,
  "invokedAt": "2026-02-19T21:05:05Z",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "ipAddress": "10.0.0.5"
}
```

**IMPORTANT**: No UPDATE or DELETE operations on this collection. MongoDB user account used by the app has INSERT + READ only on this collection.

### Indices
```javascript
db.tool_execution_logs.createIndex({ tenantId: 1, executionId: 1 })
db.tool_execution_logs.createIndex({ tenantId: 1, agentId: 1, invokedAt: -1 })
db.tool_execution_logs.createIndex({ tenantId: 1, isSuccess: 1, invokedAt: -1 })
// TTL for compliance retention (7 years for fintech = 220M seconds)
db.tool_execution_logs.createIndex({ invokedAt: 1 }, { expireAfterSeconds: 220752000 })
```

---

## `tenants` Collection

```json
{
  "_id": "ObjectId",
  "tenantId": "674abc123...",
  "slug": "acme-corp",
  "displayName": "ACME Corporation",
  "tier": "Professional",
  "isActive": true,
  "version": 2,
  "createdAt": "2026-01-01T00:00:00Z",
  "updatedAt": "2026-02-19T00:00:00Z",
  "createdBy": "platform_admin",
  "quota": {
    "maxAgents": 25,
    "maxExecutionsPerDay": 5000,
    "maxToolsPerAgent": 20,
    "maxTokensPerMonth": 10000000,
    "maxConcurrentExecutions": 10
  },
  "settings": {
    "allowExternalTools": false,
    "requireMfaForCriticalTools": true,
    "defaultLanguage": "en",
    "allowedIpRanges": ["10.0.0.0/8"],
    "enableAuditLog": true
  }
}
```

---

## MongoDB User Security Setup

```javascript
// App user: limited permissions
db.createUser({
  user: "agentflow_app",
  pwd: "<strong_password>",
  roles: [
    { role: "readWrite", db: "agentflow" }
  ]
})

// Restrict tool_execution_logs to insert+find only
// Use MongoDB RBAC or collection-level validation rules
db.runCommand({
  createRole: "toolLogWriter",
  privileges: [{
    resource: { db: "agentflow", collection: "tool_execution_logs" },
    actions: ["insert", "find"]  // No update, no delete
  }],
  roles: []
})
```
