# Runbook — Global #4 (MCP real) + #5 (MAF/A2A)

## Objetivo
Validar en local que:
1. El runtime no usa rutas mock/stub en código productivo.
2. MCP discovery + invoke funciona contra servidor HTTP real.
3. MAF behavior está controlado por contrato (sin respuestas fake cuando está deshabilitado).

## Prerrequisitos
- .NET SDK instalado
- Node/npm instalado
- Puerto `3501` libre

## Ejecución rápida
```bash
cd /home/blad/labs/ai-agent-flow
chmod +x scripts/run-global-45-local.sh
./scripts/run-global-45-local.sh
```

## Qué valida
1. `scripts/quality/no-mock-runtime.sh`
2. `tools/mcp-test-server` (`/health`, `/tools`, `/invoke`)
3. Tests de integración `MafAndMcpContractsTests`

## Evidencia mínima a guardar
- salida del script
- resultado de tests (`Passed`)
- commit hash de la corrida

## Troubleshooting
- Si puerto 3501 está ocupado, exporta `MCP_TEST_PORT=3511` y ajusta `BASE_URL`.
- Si falla npm install por red, ejecutar de nuevo con conectividad.
- Si fallan tests MAF/MCP, revisar `tests/AgentFlow.Tests.Integration/Orchestration/MafAndMcpContractsTests.cs`.
