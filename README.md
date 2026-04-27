# AgentFlow

Framework modular para construir, ejecutar y gobernar agentes de IA empresariales en .NET, con APIs, motor de ejecución, políticas, evaluación y frontend de operación.

## ¿Qué hace este proyecto?

AgentFlow permite definir agentes como configuración (JSON/DSL), ejecutar ciclos de razonamiento con herramientas, aplicar guardrails/políticas, y observar resultados con trazabilidad.

Incluye:
- **Backend en .NET 9** con API, worker y módulos desacoplados.
- **Motor de ejecución** para flujos tipo Think → Plan → Act → Observe.
- **Gobernanza empresarial**: políticas por segmento, checkpoints HITL y evaluación shadow/champion-challenger.
- **Extensibilidad** por plugins de herramientas y enrutamiento de modelos.
- **Frontend React** para operación (dashboard, agentes, ejecuciones, checkpoints) y **Studio** para diseño visual de flujos.

## ¿Por qué existe? ¿Qué problema resuelve?

Muchos equipos logran prototipos de agentes, pero fallan al llevarlos a producción por:
- falta de control de riesgo,
- poca trazabilidad de decisiones,
- integración limitada con herramientas,
- y ausencia de ciclo de mejora continua.

AgentFlow existe para cerrar ese gap: combina **velocidad de construcción** con **controles operativos y de compliance** para entornos reales.

## Objetivos y casos de uso concretos

### Objetivos del proyecto
- Estandarizar cómo se diseñan y versionan agentes.
- Ejecutar agentes con límites de costo/latencia y políticas explícitas.
- Medir calidad y habilitar despliegues progresivos (canary/shadow).
- Permitir supervisión humana en decisiones sensibles (HITL).

### Escenarios donde es útil
- **Soporte al cliente** con herramientas internas (tickets, base de conocimiento).
- **Flujos de riesgo/finanzas** donde algunas acciones requieren aprobación humana.
- **Automatización de operaciones** con múltiples agentes y herramientas REST/SQL.
- **Experimentación controlada** para comparar prompts/modelos sin impactar producción.

### Inputs y outputs
- **Inputs**:
  - Definiciones de agentes (`agents/*.json`),
  - prompts y políticas,
  - mensaje/contexto del usuario,
  - configuración de herramientas y modelos.
- **Outputs**:
  - respuesta del agente,
  - traza de ejecución (steps + rationale + tool I/O),
  - métricas (tokens, latencia, calidad),
  - checkpoints para revisión humana cuando aplica.

### Diferenciadores frente a otros frameworks
- Arquitectura modular orientada a enterprise governance.
- HITL y evaluación shadow integradas al flujo principal.
- Policies + segmentación + routing en el mismo stack.
- Base sólida .NET para equipos que operan en ecosistema Microsoft.

## Demo mínima (5–10 min)

> Objetivo: correr pruebas unitarias para validar que el core está funcional sin levantar toda la plataforma.

1. Clona el repositorio y entra al proyecto.
2. Ejecuta tests unitarios:

```bash
dotnet test tests/AgentFlow.Tests.Unit/AgentFlow.Tests.Unit.csproj
```

3. Resultado esperado:
- Todas las pruebas en verde.
- Validación de componentes clave (DSL, políticas, motor, evaluación).

Si quieres una demo visual completa (backend + frontend), revisa [`DEMO-INSTRUCTIONS.md`](./DEMO-INSTRUCTIONS.md).

## Guía de instalación y ejemplo rápido

### Requisitos
- **.NET SDK 9.0+**
- **Node.js 20+** (para frontend)
- **Docker** (opcional pero recomendado para MongoDB/Redis)
- **MongoDB** y **Redis** (para ejecución completa de API/Worker)

### Instalación básica

```bash
# 1) Restaurar dependencias .NET
dotnet restore AgentFlow.sln

# 2) (Opcional) instalar frontend
cd frontend/aiagent_flow && npm install
```

### Ejemplo rápido: validación local del core

```bash
dotnet test tests/AgentFlow.Tests.Unit/AgentFlow.Tests.Unit.csproj
```

**Salida esperada:**
- `Passed` en suites de Domain, DSL, Engine, Policy y Evaluation.

## Estado actual (validación reciente)

### Backend
- ✅ Build API en verde (`dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj`)
- ✅ Tests unitarios en verde
- ✅ Tests de integración en verde
- ✅ Guardrail `no-mock-runtime` activo y pasando

### Frontend (`frontend/aiagent_flow`)
- ✅ Lint en verde (con warnings no bloqueantes de hooks)
- ✅ Build en verde
- ✅ Tests smoke (`vitest`) en verde
- ✅ Tenant dinámico (sin hardcode principal `tenant-1` en páginas core)
- ✅ MCP Console operativa (discovery + invoke)
- ✅ Tools operativas (invoke desde UI)
- ✅ Policies operativas (create/publish + edición de reglas)
- ✅ Settings persistente (GET/PUT por tenant)

### Nota sobre mensajes de error vistos en logs
Se observaron errores intermedios de compilación por ambigüedad de tipo (`PolicySetDefinition`) durante desarrollo.
Esos errores ya fueron corregidos y el estado actual está validado en verde.

## Roadmap y progreso

Consulta [`PROGRESS.md`](./PROGRESS.md) para ver:
- hitos recientes,
- prioridades actuales,
- próximos entregables,
- y formas concretas de colaborar.

## Licencia

Este proyecto se distribuye bajo licencia **MIT**. Revisa el archivo [`LICENSE`](./LICENSE).
