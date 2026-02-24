# 🛡️ Resilience & Security Upgrade: AgentFlow Core

This document details the enterprise-grade upgrades implemented to ensure the reliability and security of AgentFlow executions, specifically focusing on the **Self-Healing Brain** and the **PII Redaction Engine**.

---

## 🧠 1. Self-Healing Brain Logic
**Implemented in:** `AgentFlow.Core.Engine.SemanticKernelBrain`

### 🔍 The Problem
LLMs often exhibit non-deterministic formatting behaviors even when JSON output is requested. Common issues include:
*   Wrapping JSON in Markdown code blocks (e.g., ` ```json ... ``` `).
*   Adding preamble or postamble text.
*   Minor JSON syntax inconsistencies that break standard parsers.

### 🛠 The Solution: Structural Resilience
We implemented a self-healing parser that:
1.  **Cleans Response Patterns:** Strips Markdown wrappers and extra whitespace before parsing.
2.  **Graceful Fallback:** If parsing still fails (malformed JSON), the system doesn't throw an exception. Instead, it treats the entire response as a `ProvideFinalAnswer` decision.
3.  **Outcome:** Increases the success rate of complex autonomous loops in production environments where LLM consistency varies.

---

## 🔒 2. Enterprise PII Redaction Engine
**Implemented in:** `AgentFlow.Policy.PiiRedactionEvaluator`

### 🔍 The Problem
Sensitive data leakage (PII - Personally Identifiable Information) is the biggest blocker for AI adoption in regulated industries (Fintech, Healthcare).

### 🛡 The Solution: Sovereign Boundary Control
A new `IPolicyEvaluator` was created to intercept PII at multiple loop checkpoints:
*   **Checkpoint Analysis:** Scans `PostLLM`, `PreTool`, and `PreResponse`.
*   **Pattern Detection:** Identifies Credit Cards, Emails, SSNs, and Phone Numbers using high-precision regex patterns.
*   **Policy Actions:**
    *   **Block:** Prevents the response from reaching the user or a tool.
    *   **Escalate:** Pauses execution and creates a `Checkpoint` for human review.
    *   **Shadow:** Logs the violation without blocking, useful for auditing.
*   **Evidence Masking:** Violations recorded in the audit log are automatically masked (e.g., `45xx-xxxx-xxxx-1234`) to ensure the audit log itself doesn't become a security risk.

---

## 🚦 Integration with Governance Framework
These features are not hardcoded but governed by the **AgentFlow DSL**:
1.  **Security Policies:** Defined in the `PolicySet` and applied per-agent.
2.  **Runtime Config:** Resilience settings are part of the `AgentDefinition`.

---
*Date of implementation: 2026-02-23*
*Status: Production Ready*
