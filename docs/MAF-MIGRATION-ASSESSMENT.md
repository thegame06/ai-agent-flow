# Validación de migración: Semantic Kernel → Microsoft Agent Framework (MAF + MCP)

> Fecha: 2026-02-23  
> Alcance revisado: `src/AgentFlow.Api`, `src/AgentFlow.Core.Engine`, `src/AgentFlow.Abstractions`, `docs`.

## 1) Resumen ejecutivo

Sí, **la migración es viable** y el diseño actual de AgentFlow la facilita por su arquitectura modular (`IAgentBrain` como contrato de desacoplamiento).  
No es un cambio “drop-in” al 100%: hay impactos relevantes en runtime, herramientas/plugins, observabilidad y pruebas de regresión.

**Conclusión práctica:**
- **Se puede manejar lo nuevo** (MAF y MCP) sin reescribir todo el sistema.
- Conviene hacer una **migración por fases** con feature flags y operación dual temporal (SK y MAF coexistiendo).
- El mayor riesgo no es compilar, sino **preservar comportamiento** (seguridad, trazabilidad, costos, latencia).

---

## 2) Validación del estado actual del proyecto

### 2.1 Dependencias y acoplamientos encontrados

1. El motor cognitivo actual usa Semantic Kernel explícitamente:
   - `Microsoft.SemanticKernel` en `AgentFlow.Core.Engine.csproj`.
   - `SemanticKernelBrain` implementa `IAgentBrain`.
2. La API construye el `Kernel` y lo inyecta vía DI en `DependencyInjection.cs`.
3. La configuración productiva está modelada bajo sección `SemanticKernel:*` en `appsettings.json`.
4. Hay documentación histórica que defiende mantener SK, por lo que se requiere alinear documentación al nuevo rumbo.

### 2.2 Señales positivas para migrar rápido

- **Abstracción correcta ya existente:** `IAgentBrain` separa el runtime del proveedor LLM.
- **Lógica de gobierno fuera del brain:** políticas, auth, checkpoints, memoria y auditoría no dependen directamente de SK.
- **Inyección por DI:** permite cambiar implementación por configuración sin alterar todo el dominio.

### 2.3 Señales de riesgo técnico

- `SemanticKernelBrain` mezcla:
  - construcción de prompts,
  - ejecución de chat,
  - parseo de JSON,
  - extracción de metadata de tokens.

  Al migrar, estas piezas pueden cambiar de contrato y formato de salida.

---

## 3) Impactos graves potenciales (si se migra sin control)

### 3.1 Compatibilidad funcional

- Cambios en formato/respuesta del modelo pueden romper `ParseThinkResult` y `ParseObserveResult`.
- Diferencias en metadata de uso/token pueden afectar métricas y costos reportados.

### 3.2 Seguridad y gobernanza

- Si MCP se conecta a servidores externos sin política fuerte:
  - riesgo de exfiltración de datos,
  - ejecución de herramientas no autorizadas,
  - aumento de superficie de ataque.

### 3.3 Operación y SRE

- Incremento de latencia por capa adicional de enrutamiento (agente ↔ MCP server ↔ herramienta).
- Nuevos puntos de falla (timeouts, auth, versionado de servidores MCP, compatibilidad de schemas).

### 3.4 Calidad de producto

- Posibles regresiones en comportamiento de agentes (decisiones distintas en escenarios sensibles).
- Necesidad de recalibrar evaluación champion/challenger y thresholds de calidad.

---

## 4) Ventajas reales esperadas con MAF + MCP

- **Interoperabilidad de herramientas** con protocolo estándar (MCP).
- **Menor lock-in** para integrar capacidades externas y servidores de contexto.
- **Escalabilidad organizacional:** equipos pueden publicar servidores MCP por dominio (finanzas, soporte, compliance).
- **Evolución más limpia del ecosistema de agentes** (A2A y orchestration más consistente).

---

## 5) Plan de ejecución recomendado (fases)

### Fase 0 — Preparación (1 sprint)

- Congelar baseline de calidad y costos con SK actual.
- Definir suite mínima obligatoria de regresión (Think/Observe + tool execution + HITL + policy checks).
- Introducir feature flag: `BrainProvider = SemanticKernel | MicrosoftAgentFramework`.

**Entregable:** diagnóstico medible (latencia, costo, tasa de éxito, seguridad).

### Fase 1 — Adaptador MAF sin MCP (1–2 sprints)

- Crear `MafBrain : IAgentBrain`.
- Mantener contrato actual (`ThinkContext`, `ThinkResult`, `ObserveContext`, `ObserveResult`) para no romper core.
- Registrar en DI por proveedor seleccionado.

**Objetivo:** reemplazar sólo el “músculo cognitivo” sin tocar gobernanza.

### Fase 2 — Incorporar MCP controlado (1–2 sprints)

- Diseñar `IMcpToolGateway` (whitelist por tenant y por herramienta).
- Integrar autorización previa con políticas existentes antes de cada llamada MCP.
- Añadir timeouts, retries acotados, circuit-breaker y auditoría de I/O MCP.

**Objetivo:** usar ventajas MCP sin sacrificar compliance.

### Fase 3 — Operación dual y rollout progresivo (1 sprint)

- Canary por tenant/segmento.
- Champion/Challenger: SK vs MAF en paralelo para comparar calidad y costo.
- Rollback instantáneo por flag.

**Criterio de salida:** MAF iguala o mejora KPIs clave de SK en ventanas estables.

### Fase 4 — Consolidación

- Deprecar `SemanticKernelBrain` cuando MAF esté estable.
- Limpiar configuración antigua `SemanticKernel:*`.
- Actualizar documentación técnica y runbooks de operación.

### Tabla de exit criteria por fase (1→2→3→4)

| Transición | Exit criteria obligatorios | Evidencia operativa requerida | Acción si no cumple |
|---|---|---|---|
| Fase 1 → Fase 2 | `MafBrain` implementa 100% del contrato `IAgentBrain` en tests de contrato; paridad funcional mínima en suite crítica (Think/Observe/Tool/HITL/Policy) ≥ 95%; sin incidentes Sev1/Sev2 atribuibles al brain durante 7 días. | Reporte de tests CI, bitácora de incidentes, diff de respuestas SK vs MAF en dataset de regresión. | Mantener Fase 1, corregir incompatibilidades de contrato y repetir ventana de 7 días. |
| Fase 2 → Fase 3 | MCP con allowlist por tenant habilitado; controles de auth/policy/auditoría activos en 100% de llamadas MCP; error rate MCP p95 ≤ 2%; timeouts con retry/circuit-breaker validados en caos controlado. | Evidencia de auditoría MCP, métricas de resiliencia, reporte de pruebas de falla (timeout/schema mismatch/unavailable server). | Bloquear canary, endurecer políticas/resiliencia y repetir pruebas de caos. |
| Fase 3 → Fase 4 | Reglas champion/challenger cumplidas por tenant/segmento (ver sección 6); rollback probado y ejecutable < 15 min; MAF supera umbral mínimo en calidad, latencia, costo, confiabilidad y seguridad por ventana estable. | Dashboard de KPIs por tenant, acta de canary, simulacro de rollback exitoso. | Extender operación dual, ajustar prompts/herramientas/políticas, repetir ventana de evaluación. |
| Fase 4 → Cierre | `SemanticKernel:*` deprecado sin dependencias activas; documentación/runbooks actualizados y aprobados; 0 tenants críticos pendientes de migrar. | PRs de limpieza, changelog, checklist de adopción firmado por SRE + Seguridad + Producto. | Mantener coexistencia controlada hasta cerrar brechas de adopción. |

---

## 6) KPIs de migración

> Objetivo: eliminar ambigüedad en promoción/rollback durante coexistencia SK (champion) vs MAF (challenger), con medición por tenant y por segmento.

### 6.1 Métricas obligatorias por dimensión

| Dimensión | Métrica obligatoria | Fórmula | Fuente de datos | Frecuencia | Ventana temporal | Umbral mínimo | Criterio de rollback |
|---|---|---|---|---|---|---|---|
| Calidad | **Task Success Rate (TSR)** | `TSR = casos_exitosos / casos_totales` | Evaluaciones offline + outcomes de ejecución en `AgentExecution`/tests E2E. | Diario | Rolling 7 días por tenant/segmento | `TSR_MAF >= TSR_SK - 1.0 pp` | Si `TSR_MAF < TSR_SK - 2.0 pp` por 2 días consecutivos en un segmento crítico. |
| Calidad | **Tool Correctness Rate (TCR)** | `TCR = llamadas_tool_correctas / llamadas_tool_totales` | Auditoría de tool calls + validadores de contrato de salida. | Diario | Rolling 7 días | `TCR_MAF >= 98%` y `delta vs SK >= -1.0 pp` | Si `TCR_MAF < 97%` o hay error semántico crítico en tool financiera/compliance. |
| Latencia | **p95 End-to-End Latency** | `p95(latencia_respuesta_ms)` | Telemetría de spans (`AgentFlowTelemetry`) API→brain→tool. | Cada 15 min + consolidado diario | Rolling 24h y 7 días | `p95_MAF <= p95_SK * 1.10` | Si `p95_MAF > p95_SK * 1.20` durante 4 intervalos de 15 min o 2 días seguidos en 24h. |
| Costo | **Costo por interacción exitosa (CPE)** | `CPE = costo_total_usd / interacciones_exitosas` | Metadata de tokens/proveedor + costo de tool/MCP por ejecución. | Diario | Rolling 7 días | `CPE_MAF <= CPE_SK * 1.05` | Si `CPE_MAF > CPE_SK * 1.10` por 3 días o supera presupuesto diario del tenant. |
| Confiabilidad | **Success without Retry (SWR)** | `SWR = respuestas_ok_sin_retry / respuestas_totales` | Logs de retries/timeouts/circuit-breaker por brain y MCP. | Diario | Rolling 7 días | `SWR_MAF >= 97%` | Si `SWR_MAF < 95%` por 2 días o apertura de circuit-breaker > 5% de requests. |
| Confiabilidad | **Error Budget Burn Rate** | `burn_rate = errores_observados / presupuesto_errores_periodo` | SLI/SLO de disponibilidad y tasa de error por tenant. | Horario + diario | 1h y 24h | `burn_rate_24h <= 1.0` | Si `burn_rate_1h > 2.0` o `burn_rate_24h > 1.0` en segmento gold. |
| Seguridad | **Policy Enforcement Coverage (PEC)** | `PEC = llamadas_con_policy_check / llamadas_totales` | Trazas de `PolicyEngine` + auditoría de handoff/tool/MCP. | Diario | Rolling 7 días | `PEC = 100%` | Cualquier valor `<100%` dispara rollback inmediato del segmento afectado. |
| Seguridad | **Incident Rate Sev1/Sev2 (IR)** | `IR = incidentes_sev1_sev2 / 1000_interacciones` | SIEM + gestión de incidentes + auditoría WORM. | Diario | Rolling 30 días | `IR_MAF <= IR_SK` y `IR=0` en canary inicial | Cualquier incidente Sev1 atribuible a MAF/MCP implica rollback inmediato del tenant. |

### 6.2 Reglas champion/challenger por tenant/segmento

- **Unidad de decisión:** `tenant_id + segmento` (ej. `enterprise-es`, `smb-en`).
- **Champion inicial:** SK. **Challenger:** MAF.
- **Conjunto de métricas evaluadas para promoción:** TSR, TCR, p95 Latency, CPE, SWR, PEC (6 métricas núcleo).

**Regla estándar de promoción (Fase 3):**
- Promover MAF a champion si:
  1. MAF supera o iguala a SK en **al menos 5 de 6 métricas núcleo** (**X=5, Y=6**),
  2. durante **N=7 días consecutivos**,
  3. sin violar guardrails críticos (PEC=100%, 0 Sev1, burn_rate_24h <= 1.0).

**Regla acelerada para segmentos no críticos:**
- Promoción con **4 de 6 métricas** durante **5 días** si costo y latencia mejoran ≥5% y no hay incidentes de seguridad.

**Regla de despromoción (rollback post-promoción):**
- Volver temporalmente a SK en un segmento si ocurre cualquiera:
  - 2 brechas consecutivas de umbral en calidad (TSR/TCR),
  - 1 brecha severa en seguridad (PEC<100% o incidente Sev1),
  - 24h con burn_rate>1.5 tras promoción.

**Regla de freeze operativo:**
- Si >20% de segmentos en un tenant incumplen umbrales la misma semana, pausar nuevas promociones y abrir RCA obligatoria.

---

## 7) Cambios concretos por módulo

- `src/AgentFlow.Core.Engine`
  - Nuevo `MafBrain` implementando `IAgentBrain`.
  - Extraer utilidades de parseo/validación JSON para reutilizar entre proveedores.

- `src/AgentFlow.Api`
  - Extender `DependencyInjection` para resolver brain por `BrainProvider`.
  - Añadir configuración `MicrosoftAgentFramework:*` y `Mcp:*`.

- `src/AgentFlow.Policy` y `src/AgentFlow.Security`
  - Reusar controles existentes para autorizar invocaciones MCP.
  - Añadir reglas explícitas de data classification para herramientas remotas.

- `src/AgentFlow.Observability`
  - Instrumentar spans/métricas MCP (latencia por servidor, error rate, payload size, retries).

- `tests/*`
  - Pruebas de contrato `IAgentBrain` (misma semántica esperada para SK y MAF).
  - Pruebas de resiliencia MCP (timeout, schema mismatch, unavailable server).

---

## 8) Riesgos y mitigaciones

- **Riesgo:** deriva funcional de respuestas entre SK y MAF.  
  **Mitigación:** golden tests + evaluación shadow obligatoria por caso crítico.

- **Riesgo:** expansión de superficie de ataque vía MCP.  
  **Mitigación:** allowlist por tenant, secrets aislados, auditoría WORM de llamadas.

- **Riesgo:** sobrecostos por mala configuración de servidores MCP.  
  **Mitigación:** presupuestos por ejecución, límites por herramienta, alertas de costo.

---

## 9) Respuesta directa a la pregunta de negocio

> “¿En teoría sería fácil porque son módulos?”

**Sí, relativamente fácil a nivel estructural**, porque el proyecto ya está bien modularizado en la interfaz `IAgentBrain`.  
**No es trivial a nivel operativo**: hay que controlar regresión funcional, seguridad y observabilidad para evitar impactos graves.

Recomendación: ejecutar migración incremental en 4 fases, con operación dual y rollback por feature flag.


## 10) Documento complementario

Para diseño detallado de coexistencia SK+MAF, ubicación de MCP en la arquitectura y patrón `Add/Use` por extensiones, ver: `docs/MCP-SK-MAF-MODULAR-ARCHITECTURE.md`.
