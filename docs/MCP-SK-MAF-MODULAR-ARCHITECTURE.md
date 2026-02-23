# Diseño objetivo: SK + MAF con MCP como estándar (arquitectura modular)

> Este documento aterriza **dónde entra cada pieza (MCP, Semantic Kernel, MAF)** en AgentFlow y cómo habilitar motores del brain por extensión (`Add/Use SemanticKernel`, `Add/Use MAF`) sin romper lo actual.

## 1) ¿Dónde entra cada tecnología en nuestro sistema?

## 1.1 Semantic Kernel (SK)

**Rol recomendado en AgentFlow:**
- Motor de inferencia/orquestación del `IAgentBrain` para casos actuales.
- Compatibilidad directa con el runtime ya implementado (`SemanticKernelBrain`).

**Dónde vive:**
- Capa `Core.Engine` (implementación del brain).
- Registro DI en `Api` (extensión dedicada SK).

## 1.2 Microsoft Agent Framework (MAF)

**Rol recomendado en AgentFlow:**
- Motor alternativo del `IAgentBrain` para escenarios de colaboración de agentes, A2A, y capacidades nativas de framework de agentes Microsoft.

**Dónde vive:**
- Capa `Core.Engine` (nuevo `MafBrain : IAgentBrain`).
- Registro DI en `Api` (extensión dedicada MAF).

## 1.3 MCP (Model Context Protocol)

**Rol recomendado en AgentFlow:**
- **Estándar de integración de herramientas/contexto**, independiente del brain.
- Debe operar como “bus de herramientas” (tool gateway) para que SK y MAF consuman los mismos servidores MCP bajo las mismas políticas.

**Dónde vive:**
- Capa de integración transversal (`Extensions` / `Infrastructure`), no acoplada a un solo brain.
- Interceptada por `Policy` y `Security` antes de ejecutar cualquier tool-call remota.

---

## 2) Cómo se verá en AgentFlow (arquitectura objetivo)

```text
                   ┌───────────────────────────────────────────────┐
                   │                AgentFlow API                  │
                   │   AddAgentFlow() + AddAgentBrains()          │
                   └───────────────────────────────────────────────┘
                                      │
                                      ▼
                   ┌───────────────────────────────────────────────┐
                   │              IAgentBrain (contrato)          │
                   └───────────────────────────────────────────────┘
                      │                                  │
      (Add/Use SK)    ▼                                  ▼   (Add/Use MAF)
             ┌──────────────────────┐          ┌──────────────────────┐
             │  SemanticKernelBrain │          │       MafBrain       │
             └──────────────────────┘          └──────────────────────┘
                      │                                  │
                      └──────────────┬───────────────────┘
                                     ▼
                    ┌────────────────────────────────────┐
                    │   IMcpToolGateway (estándar MCP)  │
                    └────────────────────────────────────┘
                                     │
                    ┌────────────────────────────────────┐
                    │  Policy + Security + Audit/WORM    │
                    └────────────────────────────────────┘
                                     │
                    ┌────────────────────────────────────┐
                    │   MCP Servers (CRM, ERP, Risk...) │
                    └────────────────────────────────────┘
```

---

## 3) ¿Hay ventajas (y “ventas” internas de negocio)?

Sí, en dos niveles:

### 3.1 Ventajas técnicas

- **Desacoplo real del brain:** SK y MAF comparten contrato (`IAgentBrain`).
- **Menos lock-in:** cambiar brain por caso de uso sin rehacer políticas ni memoria.
- **Estandarización de tools con MCP:** mismo camino para herramientas, sin importar brain.
- **Rollout seguro:** convivencia SK/MAF en paralelo con feature flags.

### 3.2 Ventajas de producto/negocio (“ventas”)

- **Time-to-market menor:** mantener SK donde ya funciona, activar MAF sólo donde aporta.
- **Riesgo controlado:** migración gradual reduce incidentes en producción.
- **Mensaje comercial sólido:** “plataforma agnóstica de motor + estándar MCP”, útil para enterprise.
- **Escalabilidad organizacional:** equipos de dominio publican servidores MCP reutilizables.

---

## 4) Estrategia modular pedida (Add/Use por extensión)

## 4.1 API de extensiones objetivo

```csharp
services.AddAgentFlow(configuration)
        .AddAgentBrains(options =>
        {
            options.DefaultBrain = BrainProvider.SemanticKernel; // o MicrosoftAgentFramework
        })
        .AddSemanticKernelBrain(configuration)
        .AddMafBrain(configuration)
        .AddMcpGateway(configuration);
```

Y para cambiar por entorno/caso de uso:

```json
{
  "AgentBrain": {
    "DefaultProvider": "SemanticKernel"
  }
}
```

o por agente/tenant:
- `agent.Runtime.BrainProvider = "MicrosoftAgentFramework"`

## 4.2 Reglas de diseño

1. **`IAgentBrain` se mantiene estable**.
2. **SK y MAF se registran por separado** (no if/else gigante en un solo archivo).
3. **MCP siempre detrás de políticas y seguridad**.
4. **Selección del provider por resolución en runtime** (tenant, agent, segmento, risk profile).

---

## 5) Plan técnico concreto para implementar esto

### Fase A — Modularización de DI (sin cambiar comportamiento)

- Extraer de `DependencyInjection` métodos dedicados:
  - `AddSemanticKernelBrain(...)`
  - `AddAgentBrainResolver(...)`
- Introducir opciones:
  - `AgentBrainOptions`
  - `BrainProvider` enum

**Resultado:** SK sigue como default, pero ya queda preparado para dual-engine.

### Fase B — Incorporar `MafBrain`

- Crear implementación `MafBrain : IAgentBrain`.
- Garantizar paridad de contrato (`ThinkResult`, `ObserveResult`).
- Activar con flag por tenant/agente.

### Fase C — MCP como estándar transversal

- Crear `IMcpToolGateway` y `McpToolGateway`.
- Tool calls pasan por:
  1) autorización (`Policy/Security`),
  2) ejecución MCP,
  3) auditoría (input/output + metadata).

### Fase D — Operación dual y decisión final

- Champion/Challenger SK vs MAF.
- KPIs mínimos:
  - calidad respuesta,
  - latencia p95,
  - costo por ejecución,
  - incidentes de seguridad.
- Cuando MAF supere umbral, se promueve provider por segmento.

---

## 6) Riesgos críticos y control

- **Drift funcional entre SK y MAF:** usar tests de contrato del brain + golden prompts.
- **Riesgo de exfiltración vía MCP:** allowlist por tenant, clasificación de datos, secretos rotados.
- **Sobrecoste operacional:** budgets por ejecución y alertas por tool-server.

---

## 7) Resumen ejecutivo final

- Sí, **podemos mantener SK y MAF en paralelo** de forma modular.
- Sí, **MCP debe ser estándar** de tools/contexto para ambos motores.
- Sí, **se puede cambiar de un brain a otro por caso de uso** con extensiones `Add/Use` y selección por configuración/tenant/agente.
- Recomendación: arrancar por modularizar DI (Fase A) y luego habilitar MAF + MCP gradualmente.
