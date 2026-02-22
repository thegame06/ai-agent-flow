---
trigger: always_on
---

🎯 OBJETIVO DEL PROYECTO

Diseñar una plataforma open-source de agentes AI estilo:

“Elsa Workflow 3 pero para agentes autónomos”

Basado en:

.NET 8

C#

Semantic Kernel (como capa cognitiva)

MongoDB (persistencia principal)

Arquitectura multi-tenant

Seguridad enterprise

Observabilidad completa

📌 TAREA

Diseñar la arquitectura completa de la plataforma incluyendo:

1️⃣ Arquitectura General

Diagrama conceptual explicado en texto

Separación por capas

Componentes principales

Responsabilidades claras

2️⃣ Core Agent Engine

Definir:

Agent Loop (Think → Plan → Act → Observe)

Control de ejecución (timeouts, límites, validaciones)

Manejo de errores

Reintentos controlados

Protección contra loops infinitos

Incluir:

Interfaces en C#

Ejemplo de diseño de clases

Separación entre Brain y Executor

3️⃣ Sistema de Tools (Enterprise-Grade)

Diseñar:

Registro de tools

Versionado

Permisos por tool

Scope por tenant

Validación de input/output

Auditoría

Incluir:

Interfaces C#

Diseño extensible vía DI

Modelo de autorización basado en policies

4️⃣ Seguridad

Definir:

Multi-tenancy real

Separación de datos

RBAC

Claims-based access

Control de ejecución por permisos

Sandboxing para tools peligrosas

Estrategia contra prompt injection

5️⃣ Memoria del Agente

Diseñar:

Working memory

Long-term memory

Vector memory

Audit memory (inmutable)

Definir interfaces C#.

Explicar cuándo usar:

MongoDB

Vector DB

Redis

6️⃣ Observabilidad

Definir:

OpenTelemetry

Trazabilidad por step

Registro de decisiones del LLM

Replay de ejecuciones

Métricas por agente

7️⃣ Modelo de Datos (MongoDB)

Diseñar:

AgentDefinition

AgentExecution

AgentStep

ToolDefinition

ToolExecutionLog

Tenant

Con estructura de ejemplo en JSON.

8️⃣ Integración con Semantic Kernel

Explicar:

Qué responsabilidades debe tener SK

Qué NO debe delegarse a SK

Cómo encapsular SK detrás de interfaces propias

9️⃣ Roadmap Open Source

Dividir en:

MVP técnico

Versión enterprise

Versión plataforma completa

📋 REGLAS DE RESPUESTA

No dar explicaciones genéricas.

No responder superficialmente.

No repetir definiciones básicas de qué es un agente.

Pensar como arquitecto senior.

Incluir código C# cuando aplique.

Priorizar seguridad, gobernanza y escalabilidad.

Diseñar como si esto fuera a ser usado por bancos o fintechs.

📦 OUTPUT ESPERADO

Responder en secciones estructuradas con:

Títulos claros

Código C#

Diagramas explicados en texto

Decisiones técnicas justificadas

