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

---

## 6) Cambios concretos por módulo

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

## 7) Riesgos y mitigaciones

- **Riesgo:** deriva funcional de respuestas entre SK y MAF.  
  **Mitigación:** golden tests + evaluación shadow obligatoria por caso crítico.

- **Riesgo:** expansión de superficie de ataque vía MCP.  
  **Mitigación:** allowlist por tenant, secrets aislados, auditoría WORM de llamadas.

- **Riesgo:** sobrecostos por mala configuración de servidores MCP.  
  **Mitigación:** presupuestos por ejecución, límites por herramienta, alertas de costo.

---

## 8) Respuesta directa a la pregunta de negocio

> “¿En teoría sería fácil porque son módulos?”

**Sí, relativamente fácil a nivel estructural**, porque el proyecto ya está bien modularizado en la interfaz `IAgentBrain`.  
**No es trivial a nivel operativo**: hay que controlar regresión funcional, seguridad y observabilidad para evitar impactos graves.

Recomendación: ejecutar migración incremental en 4 fases, con operación dual y rollback por feature flag.


## 9) Documento complementario

Para diseño detallado de coexistencia SK+MAF, ubicación de MCP en la arquitectura y patrón `Add/Use` por extensiones, ver: `docs/MCP-SK-MAF-MODULAR-ARCHITECTURE.md`.
