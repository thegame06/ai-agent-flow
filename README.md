# AgentFlow

Framework modular para construir, operar y gobernar agentes de IA empresariales en .NET, organizado por suites de producto: **Studio**, **Connect** y **Control**.

## ¿Qué hace este proyecto?

AgentFlow permite definir agentes como configuración (JSON/DSL), ejecutar ciclos de razonamiento con herramientas, aplicar guardrails/políticas, y observar resultados con trazabilidad.

Incluye:
- **Backend en .NET 9** con API, worker y módulos desacoplados.
- **Motor de ejecución** para flujos tipo Think → Plan → Act → Observe.
- **Gobernanza empresarial**: políticas por segmento, checkpoints HITL y evaluación shadow/champion-challenger.
- **Extensibilidad** por plugins de herramientas y enrutamiento de modelos.
- **Producto por suites**:
  - **Studio**: diseño, publicación y evaluación de agentes.
  - **Connect**: operación de canales, sesiones y ejecuciones en vivo.
  - **Control**: seguridad, compliance, observabilidad y administración.

## Modelo de producto (nuevo)

La narrativa oficial de AgentFlow se organiza en tres suites:

1. **Studio (Build Time)**: donde equipos de producto y builders diseñan, prueban y publican capacidades de agentes.
2. **Connect (Run Time)**: donde equipos de operación gestionan conversaciones, canales y ejecución en producción.
3. **Control (Governance Time)**: donde seguridad, compliance y plataforma gobiernan riesgos, accesos y evidencia.

Documentos clave del modelo:
- Arquitectura de producto: [`docs/PRODUCT-ARCHITECTURE.md`](./docs/PRODUCT-ARCHITECTURE.md)
- Matriz de capacidades: [`docs/CAPABILITY-MATRIX.md`](./docs/CAPABILITY-MATRIX.md)
- Roadmap trimestral por suite: [`docs/ROADMAP-QUARTERLY-STUDIO-CONNECT-CONTROL.md`](./docs/ROADMAP-QUARTERLY-STUDIO-CONNECT-CONTROL.md)
- Deck narrativo: [`docs/PRODUCT-MODEL-DECK.md`](./docs/PRODUCT-MODEL-DECK.md)

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

## Roadmap y progreso

Consulta [`docs/ROADMAP-QUARTERLY-STUDIO-CONNECT-CONTROL.md`](./docs/ROADMAP-QUARTERLY-STUDIO-CONNECT-CONTROL.md) para objetivos trimestrales por suite y [`PROGRESS.md`](./PROGRESS.md) para hitos de ejecución.

## Licencia

Este proyecto se distribuye bajo licencia **MIT**. Revisa el archivo [`LICENSE`](./LICENSE).
