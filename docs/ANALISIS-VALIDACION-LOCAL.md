# Análisis y validación local — AgentFlow

Fecha: 2026-11-26 · Commit analizado: `ec8f155` · Entorno: Windows 11, .NET SDK 10.0.302 (runtime 9.0.18), Node 24.19, Docker 29.6

## 1. Resumen ejecutivo

| Área | Resultado |
|---|---|
| Build .NET (`AgentFlow.sln`, 23 proyectos net9.0) | ✅ 0 errores · ⚠️ 1341 warnings (mayoría nullability) |
| Tests unitarios backend | ✅ **280 / 280** (274 + 6 nuevos de regresión) |
| Tests integración backend | ✅ **41 / 41** |
| Frontend `npm ci` / lint / build | ✅ build OK · lint 0 errores, 11 warnings |
| Frontend tests (vitest) | ✅ **10 / 10** |
| Infra Docker (Mongo, Redis, Qdrant, NATS, MCP test) | ✅ todos `healthy` |
| API `:5000` (`/health`, login JWT, seeding) | ✅ |
| MCP Server sidecar `:3502` | ✅ `/health` OK |
| Frontend `:3039` → login → 12 páginas del dashboard | ✅ 0 llamadas API fallidas |
| Swagger JSON | ✅ 200 (246 rutas) |
| Descubrimiento de tools MCP | ✅ 79 tools registradas (77 sidecar + 2 test) |

**Conclusión:** el proyecto compila y corre en local. Los **3 defectos de runtime** y los **2 tests desactualizados** encontrados en la primera pasada ya están **corregidos y verificados** (detalle en §4). Tabla actualizada tras los fixes.

## 2. Herramientas de análisis instaladas/configuradas

| Herramienta | Uso | Artefactos |
|---|---|---|
| **CodeGraph** 1.6.0 (`codegraph init`) | Grafo SQLite de símbolos/llamadas; consulta vía MCP `codegraph_explore` o `codegraph explore "<pregunta>"` | `.codegraph/` (760 archivos, 17.7k nodos, 36k aristas, 352 rutas HTTP) |
| **graphify** 0.9.46 (`graphify update .`, AST sin LLM) | Grafo de conocimiento + comunidades + god nodes | `graphify-out/graph.html`, `graph.json`, `GRAPH_REPORT.md` (10.9k nodos, 25k aristas, 473 comunidades) |
| `.graphifyignore` | Excluye `public/`, capturas `jelou/`, `node_modules`, `bin/obj` | — |

Comandos útiles:

```powershell
graphify query "como se ejecuta un agente desde un canal"
graphify path "ChannelGateway" "AgentExecutionEngine"
graphify god-nodes --top 15
graphify update .          # tras cambios de código (sin costo LLM)
codegraph explore "AgentExecutionEngine RunLoopAsync"
codegraph sync .
```

> Nota: el grafo de graphify solo tiene extracción de **código** (AST). Los ~90 `.md` de `docs/` no tienen extracción semántica; para eso hay que correr `/graphify --update` con un backend LLM configurado.

> Sugerencia: añadir `.codegraph/` (47 MB) y `graphify-out/cache/` al `.gitignore`.

## 3. Arquitectura (resumen del grafo)

- **Tamaño:** ~72k LOC C# · ~57k LOC TS/TSX · 23 proyectos .NET + 1 SPA React (Vite + MUI, plantilla Minimals).
- **Capas .NET:** `Abstractions` / `Domain` (agregados DDD: `AgentExecution`, `ConversationThread`…) → `Application` → `Core.Engine` (motor Think→Plan→Act→Observe) → `Infrastructure` (Mongo, Redis, Qdrant, NATS, MCP gateways) → `Api` (45 controllers) + `Worker` + `McpServer` (sidecar).
- **Cerebros LLM:** `IAgentBrain` con `MafBrain` (Microsoft Agent Framework, default) y `SemanticKernelBrain`, resueltos por `AgentBrainResolver`.
- **God nodes (más acoplados):** `AgentFlowApiClient` (159, frontend), `Result` (149), `ChannelDefinition` (130), `ChannelSession` (104), `IAgentFlowMcpTool` (77), `AgentExecutionEngine` (76), `CommerceStore` (65).

### Deuda técnica destacada
- `src/AgentFlow.Core.Engine/AgentExecutionEngine.cs`: **3,366 líneas, 23 dependencias en el constructor** (10 opcionales "✅ NEW"). Es el principal punto de riesgo del motor.
- Archivos muy grandes: `WorkflowRuntime.cs` (1,780), `SeedData.cs` (1,456), `CommerceController.cs` (1,456); frontend `CommerceAdminPage.tsx` (2,964), `WorkflowVisualDesigner.tsx` (2,448).
- Cobertura frontend mínima: 3 archivos de test para ~430 archivos fuente.
- 1341 warnings de compilación (sobre todo CS86xx nullability).
- Texto de plantilla "This is an info Alert." visible en todas las páginas del dashboard.
- Valores dev en config versionada: `Jwt:SecretKey` placeholder, admin `Admin123!` en `appsettings.Development.json`, MCP `Security.Mode: Open`. Aceptable en dev; verificar que producción los sobreescriba.

## 4. Defectos encontrados

> **Estado: los puntos 1–5 están corregidos** (ver §4.1).

### Runtime (afectan la app corriendo)

1. **`AssistantBuilderController` → 500 en todos sus endpoints**
   `src/AgentFlow.Api/Controllers/AssistantBuilderController.cs:21` y `:25`: dos constructores públicos; DI lanza `InvalidOperationException: Multiple constructors accepting all given argument types`. Visible en `/dashboard/studio/workflows` (`GET /api/v1/assistant/wizard/metrics` → 500). Fix: eliminar el constructor vacío (o marcar el bueno con `[ActivatorUtilitiesConstructor]`).

2. **Swagger JSON → 500**
   `src/AgentFlow.Api/Controllers/TestStudioController.cs:188`: `[FromForm] IFormFile file` no es soportado por Swashbuckle 7. `/swagger` carga pero `/swagger/v1/swagger.json` falla. Fix: quitar `[FromForm]` del `IFormFile` o envolverlo en un DTO con `[FromForm]`.

3. **Descubrimiento de tools MCP nunca funciona en local**
   `McpDiscoveryService.cs:104` hace `server.Url + "/tools"`, y `appsettings.Development.json` define `Url: http://localhost:3501/invoke` y `:3502/invoke` → consulta `/invoke/tools` (404). Ambos servidores exponen `/tools`. Fix: separar URL base de la ruta de invocación (o derivar discovery desde la base).

### Tests desactualizados

4. `tests/AgentFlow.Tests.Integration/Commerce/CommerceControllerTests.cs:486-508`: el `InMemoryCommerceStore` del test guarda `Attributes`/`Variations` como `null`; el store real normaliza a lista vacía, y `CommerceController.ToInventoryDto` hace `.Select` sobre ellos → `ArgumentNullException`. Corregir el stub (`?? []`).
5. `frontend/aiagent_flow/src/aiagentflow/pages/workflows/store/__tests__/workflowThunks.test.ts`: el mock no define `getWizardMetrics` ni `getRuntimeProfiles`, que `workflowThunks.ts:66,75` ya usa → el thunk se rechaza.


### 4.1 Correcciones aplicadas

| # | Cambio | Verificación |
|---|---|---|
| 1 | `AssistantBuilderController`: `[ActivatorUtilitiesConstructor]` en el constructor Mongo (se conserva el vacío para modo en memoria / tests) | Nuevo test `Controller_CanBeActivatedByDependencyInjection` (falla sin el fix con el mismo error de producción); `GET /api/v1/assistant/wizard/metrics` → 200 |
| 2 | `TestStudioController.UploadAttachment`: se quita `[FromForm]` del `IFormFile` (`[ApiController]` infiere el binding) | `swagger.json` → 200; el endpoint se documenta como `multipart/form-data` binario |
| 3 | `McpDiscoveryService.BuildToolsUrl`: deriva `/tools` como hermano de la URL de invocación (misma convención que `McpController`/`AgentsController`, y soporta URL solo-host) | 5 casos nuevos en `McpDiscoveryServiceTests`; log de la API registra 79 tools MCP |
| 4 | Stub `InMemoryCommerceStore` del test normaliza listas a `[]` como el store real | Integración 41/41 |
| 5 | Mock de `workflowThunks.test.ts` añade `getWizardMetrics` y `getRuntimeProfiles` | vitest 10/10 |

### Entorno / scripts

6. `make up-local-full` **no funciona en Windows/Git Bash**: `make` no viene instalado y `scripts/local-full-up.sh` usa `ss` (solo Linux). Usar el runbook de §5.
7. `package.json` exige `node 20.x`; con Node 24 solo hay warning (`EBADENGINE`) y todo funciona.
8. Ejecutar agentes con LLM requiere credenciales (OpenAI/Azure OpenAI vía Auth Profiles o `SemanticKernel:*`); no hay ninguna configurada por defecto.

## 5. Runbook local para Windows (validado)

```powershell
# 1) Infra
docker compose -p aiagents -f docker-compose.local.yml up -d
# 2) Build + tests backend
dotnet build AgentFlow.sln -v minimal
dotnet test tests/AgentFlow.Tests.Unit --no-build
dotnet test tests/AgentFlow.Tests.Integration --no-build
```

```powershell
# 3) API (terminal 1)
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:5000"
$env:ConnectionStrings__MongoDB="mongodb://localhost:27018"; $env:ConnectionStrings__Redis="localhost:6380"
dotnet run --no-build --no-launch-profile --project src/AgentFlow.Api/AgentFlow.Api.csproj
```

```powershell
# 4) MCP Server sidecar (terminal 2, opcional)
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:3502"
$env:AgentFlowApi__BaseUrl="http://localhost:5000"; $env:ConnectionStrings__MongoDB="mongodb://localhost:27018"
dotnet run --no-build --no-launch-profile --project src/AgentFlow.McpServer/AgentFlow.McpServer.csproj
```

```powershell
# 5) Frontend (terminal 3)
cd frontend/aiagent_flow; npm ci; npx vite --strictPort --port 3039
```

URLs: frontend http://localhost:3039 (usuario dev sembrado en `appsettings.Development.json`) · API http://localhost:5000/health · Swagger UI http://localhost:5000/swagger · Qdrant http://localhost:6333/readyz · NATS http://localhost:8222

Para detener: `docker compose -p aiagents -f docker-compose.local.yml down` (conserva volúmenes).
