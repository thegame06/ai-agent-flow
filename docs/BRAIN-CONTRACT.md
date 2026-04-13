# Brain Contract (IAgentBrain) — v1.1

Este documento define el contrato canónico entre `AgentExecutionEngine` y cualquier implementación de `IAgentBrain` (SK, MAF u otras).

## 1) Contratos de datos

### `IAgentBrain`
- `ThinkAsync(ThinkContext)` **obligatorio**
- `ObserveAsync(ObserveContext)` **obligatorio**

### `ThinkContext`
**Obligatorios**
- `tenantId`
- `executionId`
- `systemPrompt`
- `userMessage`

**Opcionales / default**
- `iteration` (default `0`)
- `history` (default `[]`)
- `workingMemoryJson` (default `"{}"`)
- `availableTools` (default `[]`)
- `runtimeMode` (nullable)
- `threadSnapshot` (nullable)

### `ThinkResult`
**Obligatorio**
- `decision` (`UseTool|ProvideFinalAnswer|Checkpoint|RequestMoreContext`)

**Opcionales**
- `rationale`
- `detectedIntent`
- `nextToolName`
- `nextToolInputJson`
- `finalAnswer`
- `tokensUsed` (default `0`)
- `promptInjectionDetected` (default `false`)

### `ObserveResult`
**Obligatorio**
- `summary`

**Opcionales**
- `goalAchieved` (default `false`)
- `tokensUsed` (default `0`)

---

## 2) Invariantes semánticos

### Decisión `UseTool`
Debe emitirse cuando:
1. existe una herramienta adecuada en `availableTools`;
2. el siguiente paso requiere acción/consulta externa;
3. se puede construir un `nextToolInputJson` válido.

Invariantes:
- `nextToolName` **requerido**
- `nextToolInputJson` **requerido**
- `finalAnswer` **prohibido**

### Decisión `ProvideFinalAnswer`
Debe emitirse cuando el cerebro puede responder sin herramientas adicionales.

Invariantes:
- `finalAnswer` **requerido**
- `nextToolName` y `nextToolInputJson` **prohibidos**

### Decisión `Checkpoint`
Debe emitirse cuando:
- hay riesgo de seguridad/policy;
- falta configuración crítica (p. ej. brain deshabilitado);
- el output del modelo es inválido para contrato.

Invariantes:
- sin campos de herramienta ni `finalAnswer`.

### Decisión `RequestMoreContext`
Se usa cuando falta información del usuario para continuar.

Invariantes:
- sin campos de herramienta ni `finalAnswer`.

### Serialización de errores de contrato
Cuando un cerebro no puede parsear JSON o rompe invariantes, debe normalizar a `Checkpoint` y serializar:

```json
{
  "code": "BRAIN_CONTRACT_VIOLATION",
  "brain": "SemanticKernel|MAF|...",
  "contractType": "ThinkResult|ObserveResult",
  "errors": ["..."]
}
```

---

## 3) Versionado y compatibilidad

### Versión actual
- **v1.0**: contrato base (`ThinkContext`, `ThinkResult`, `ObserveResult`).
- **v1.1**: agrega validación semántica explícita y serialización uniforme de errores.

### Reglas
1. **Minor (`v1.x`)**: solo cambios backward compatible:
   - agregar campos opcionales;
   - ampliar telemetría sin cambiar semántica existente.
2. **Major (`v2.0`)**: cambios breaking:
   - quitar/renombrar campos;
   - cambiar significado de decisiones;
   - alterar invariantes condicionales.
3. Nuevos cerebros deben soportar al menos la última minor de la major activa.
4. Engine y tests deben validar la versión declarada antes de promoción.

---

## 4) Golden tests compartidos SK/MAF

Se define una suite común con fixtures JSON para validar:
- parseo equivalente de casos válidos;
- fallback equivalente ante JSON malformado;
- enforcement de invariantes (normalización a `Checkpoint`).

Los tests viven en integración y usan el mismo set de fixtures para ambos cerebros.

---

## 5) Validación automática en CI

CI debe ejecutar un gate dedicado (`brain-contract-check`) que corre:
1. golden tests de contrato SK/MAF;
2. prueba de comportamiento con MAF deshabilitado.

Si cualquier contrato se rompe, el pipeline falla.
