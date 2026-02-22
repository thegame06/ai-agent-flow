# AgentFlow — Enterprise Agent Blueprints (v3.2)

Documentación de referencia para casos de uso de misión crítica en sectores regulados (Fintech, Seguros, LegalTech).

## 1. Agente de Cobranza (Debt Collection)
*Ya definido en v3.1* (Enfocado en cumplimiento legal y recuperación temprana).

## 2. Agente de Aprobación de Crédito (Credit Scoring & Approval) 🦄
**Objetivo**: Automatizar la decisión de crédito integrando fuentes externas y políticas de riesgo internas.
**Valor**: Reducción del tiempo de respuesta (Time-to-Yes) y consistencia en la política de riesgo.

```json
{
  "agent": {
    "key": "credit_approver_v1",
    "version": "1.0.0",
    "role": "Analista de Riesgo Crediticio automatizado.",
    "runtime": { "mode": "hybrid", "temperature": 0.0 },
    "policies": {
      "policySetId": "risk_management_standard_v1",
      "guardrails": {
        "requireHumanReviewOnDecisionMismatch": true,
        "maxAutomaticAmount": 50000
      }
    },
    "flows": [
      {
        "name": "AnalyzeCreditApplication",
        "trigger": { "type": "event", "value": "NewApplicationSubmitted" },
        "steps": [
          { "tool": "FetchCreditBureauHistory", "required": true },
          { "tool": "EnrichWithBankingData", "required": false },
          { "tool": "EvaluateRiskScore", "required": true },
          { 
            "tool": "ApproveOrReject", 
            "guardrails": {
              "requireHumanApproval": "amount > 50000 || score < 650"
            }
          }
        ]
      }
    ]
  }
}
```

## 3. Agente PLD (Prevención de Lavado de Dinero / AML) 🛡️
**Objetivo**: Monitoreo de transacciones y detección de patrones de lavado en tiempo real.
**Valor**: Cumplimiento regulatorio bancario y prevención de multas millonarias.

```json
{
  "agent": {
    "key": "aml_sentinel_v1",
    "version": "1.1.0",
    "role": "Oficial de Cumplimiento especializado en AML/KYC.",
    "runtime": { "mode": "deterministic" },
    "authorizedTools": ["CheckSanctionsList", "AnalyzeTransactionPattern", "CreateSuspiciousActivityReport"],
    "policies": {
      "policySetId": "aml_regulatory_compliance_v3",
      "guardrails": {
        "blockingOnSanctionMatch": true,
        "escalationOnTransactionStructure": true
      }
    },
    "flows": [
      {
        "name": "TransactionScrutiny",
        "trigger": { "type": "event", "value": "LargeTransactionDetected" },
        "steps": [
          { "tool": "CheckSanctionsList", "required": true },
          { "tool": "AnalyzeTransactionPattern", "required": true },
          { 
            "tool": "CreateSuspiciousActivityReport", 
            "guardrails": { "requireHumanReview": "always" } 
          }
        ]
      }
    ]
  }
}
```

## 4. Agente de Soporte Crítico (Tier-1 Support)
*Ya definido en v3.1* (Enfocado en RAG y seguridad de datos).
