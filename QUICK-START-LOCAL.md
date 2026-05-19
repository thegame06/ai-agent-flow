# 🚀 Quick Start: Levantar AgentFlow con Makefile

**Guía rápida para desarrolladores: 2 minutos para tener todo corriendo**

---

## ✅ Prerequisitos

Asegúrate de tener instalado:
- ✅ **Docker Desktop** (para Windows) o Docker Engine (Linux/Mac)
- ✅ **.NET 9 SDK**
- ✅ **Node.js 22+**
- ✅ **Make** (viene con Git Bash en Windows)

---

## 🏃 Inicio Rápido

### Opción 1: Todo en uno (Recomendado)

```bash
cd c:\labs\aiagents
make up-local-full
```

**¿Qué hace este comando?**
1. ✅ Levanta Docker containers:
   - MongoDB (puerto 27018)
   - Redis (puerto 6380)
   - Qdrant (puerto 6333)
   - MCP Test Server (puerto 3501)
2. ✅ Compila y ejecuta AgentFlow API (.NET 9) → puerto 5000
3. ✅ Inicia Frontend (React + Vite) → puerto 3039
4. ✅ Inicia WhatsApp QR Bridge → puerto 3401

**Verifica que todo esté OK:**

```bash
# Windows (PowerShell)
curl http://localhost:5000/health          # API
curl http://localhost:3039                 # Frontend
curl http://localhost:6333/readyz          # Qdrant

# O ejecuta el script de verificación
.\scripts\test\verify-frontend-backend.ps1
```

**Abrir en navegador:**
- 🎨 **Frontend Studio**: http://localhost:3039
- 🧠 **Intent Management**: http://localhost:3039/dashboard/intents
- 🧪 **Intent Playground**: http://localhost:3039/dashboard/intents/playground
- 📥 **Inbox**: http://localhost:3039/dashboard/inbox
- 📡 **API Swagger**: http://localhost:5000/swagger

---

### Opción 2: Solo Infraestructura (sin apps)

Si solo necesitas MongoDB/Redis/Qdrant:

```bash
docker compose -f docker-compose.local.yml up -d
```

Luego ejecuta manualmente:

```bash
# Terminal 1: API
cd src/AgentFlow.Api
dotnet run

# Terminal 2: Frontend
cd frontend/aiagent_flow
npm run dev
```

---

## 🛑 Detener Todo

```bash
# Detiene stack completo (preserva datos)
make down-local-full

# O si usaste docker-compose directamente
docker compose -f docker-compose.local.yml down
```

---

## 🔄 Reiniciar (sin perder datos)

```bash
make restart-local-full
```

Esto:
1. Recompila el API
2. Mata procesos colgados
3. Reinicia todo **sin borrar volúmenes** de Docker

---

## 🧹 Limpiar Todo (⚠️ Borra Datos)

```bash
make refresh-local-full
```

**⚠️ CUIDADO**: Este comando:
1. Mata todos los procesos
2. **BORRA volúmenes de Docker** (MongoDB, Redis, Qdrant)
3. Reinicia limpio

Úsalo solo si tienes problemas graves o quieres empezar desde cero.

---

## 🧪 Testing

```bash
# Tests completos (backend + frontend)
make test-all

# Solo backend (unit + integration)
make test-backend

# Solo frontend
make test-frontend
```

---

## 🐛 Troubleshooting

### Puerto ocupado

**Error**: `Port 5000 is busy`

```bash
# Windows
Get-Process -Id (Get-NetTCPConnection -LocalPort 5000).OwningProcess | Stop-Process -Force

# Linux/Mac
lsof -ti:5000 | xargs kill -9

# Luego reintentar
make up-local-full
```

### Docker no arranca

```bash
# Ver qué falló
docker compose -f docker-compose.local.yml ps

# Ver logs
docker logs agentflow-mongo-local
docker logs agentflow-redis-local
docker logs agentflow-qdrant-local
```

### API no compila

```bash
# Ver errores detallados
dotnet build AgentFlow.sln -v detailed

# Limpiar y rebuild
dotnet clean
dotnet restore
dotnet build
```

### Ver logs en tiempo real

```bash
# Windows (PowerShell)
Get-Content -Path .agent/logs/api-full.log -Wait -Tail 50

# Linux/Mac
tail -f .agent/logs/api-full.log
```

---

## 📊 Estado de Servicios

**Verificar que todo esté corriendo:**

```bash
# Docker containers
docker ps

# Debe mostrar:
# - agentflow-mongo-local
# - agentflow-redis-local
# - agentflow-qdrant-local
# - agentflow-mcp-test
```

**Verificar salud:**

```bash
curl http://localhost:27018     # MongoDB
redis-cli -p 6380 ping          # Redis (debe retornar PONG)
curl http://localhost:6333/readyz  # Qdrant (debe retornar "ok")
curl http://localhost:5000/health  # API (debe retornar 200)
```

---

## 🎯 Flujo de Desarrollo Típico

1. **Primera vez del día**:
   ```bash
   make up-local-full
   ```

2. **Haces cambios en el código**:
   ```bash
   # El API se auto-recarga con hot reload
   # El Frontend se auto-recarga con Vite HMR
   ```

3. **Quieres reiniciar el API**:
   ```bash
   make restart-local-full
   ```

4. **Antes de hacer commit**:
   ```bash
   make qa-one-shot    # Corre todos los tests + quality checks
   ```

5. **Al terminar el día**:
   ```bash
   make down-local-full
   ```

---

## 📚 Más Comandos Útiles

```bash
make help                    # Ver todos los comandos disponibles
make check-qr CHANNEL_ID=xyz # Debug WhatsApp session
make quality-no-mock         # Validar que no hay mocks en runtime
make contract-check          # Validar contratos de interfaces
```

---

## 🌐 Entornos

| Entorno | Comando | Documentación |
|---------|---------|---------------|
| **Local** | `make up-local-full` | Este archivo |
| **Staging** | Ver CI/CD pipeline | [INFRASTRUCTURE-SETUP.md](docs/INFRASTRUCTURE-SETUP.md) |
| **Production** | Kubernetes | [INFRASTRUCTURE-SETUP.md](docs/INFRASTRUCTURE-SETUP.md) |

---

## ❓ Preguntas Frecuentes

### ¿Por qué MongoDB está en puerto 27018 y no 27017?

Para evitar conflictos si tienes MongoDB instalado localmente.

### ¿Puedo cambiar los puertos?

Sí, exporta variables de entorno:

```bash
# Windows (PowerShell)
$env:API_PORT = 5001
$env:FRONTEND_PORT = 3040

# Linux/Mac
export API_PORT=5001
export FRONTEND_PORT=3040

# Luego
make up-local-full
```

### ¿Los datos persisten?

Sí, los volúmenes de Docker persisten entre reinicios:
- `mongo_local_data`
- `redis_local_data`
- `qdrant_local_data`

Solo se borran con `make refresh-local-full` o `docker compose down -v`.

### ¿Cómo veo qué PIDs están corriendo?

```bash
cat .agent/run/api.pid
cat .agent/run/frontend.pid
cat .agent/run/qr.pid
```

### ¿Dónde están los logs?

```
.agent/logs/
├── api-full.log
├── frontend-full.log
├── qr-bridge-full.log
└── mcp-server-full.log
```

---

## 🎉 ¡Listo!

Ahora tienes AgentFlow corriendo en tu máquina local con:
- ✅ MongoDB + Redis + Qdrant
- ✅ API corriendo en http://localhost:5000
- ✅ Frontend corriendo en http://localhost:3039
- ✅ Intent Routing operacional
- ✅ Workflow Engine conectado

**Siguiente paso**: Abre http://localhost:3039/dashboard/intents y prueba el sistema! 🚀

---

**Documentación completa**: [INFRASTRUCTURE-SETUP.md](docs/INFRASTRUCTURE-SETUP.md)
