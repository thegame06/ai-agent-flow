# 🐛 Troubleshooting: Docker Desktop + Qdrant

**Guía rápida para solucionar problemas con la infraestructura Docker**

---

## 🔍 Problema: Docker Desktop no está corriendo

### Error observado:
```
error during connect: Get "http://%2F%2F.%2Fpipe%2FdockerDesktopLinuxEngine/v1.51/containers/json?all=1": 
open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.
```

### Causa:
Docker Desktop no está iniciado en Windows.

### Solución:

#### 1. Iniciar Docker Desktop

```powershell
# Opción A: Desde el menú Start
# Buscar "Docker Desktop" y ejecutar

# Opción B: Desde PowerShell
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"

# Esperar 30-60 segundos para que inicie completamente
```

#### 2. Verificar que Docker está corriendo

```powershell
# Debe retornar la versión
docker --version

# Debe mostrar información del sistema
docker info

# Debe retornar "OK"
docker ps
```

Si alguno falla, espera más tiempo o reinicia Docker Desktop.

---

## 🐳 Problema: Container de Qdrant no arranca

### Síntomas:
- `make up-local-full` se cuelga esperando Qdrant
- `docker ps` muestra Qdrant como `unhealthy` o `restarting`
- Logs muestran errores de inicialización

### Diagnóstico:

```powershell
# Ver estado de Qdrant
docker ps -a | Select-String "qdrant"

# Ver logs del container
docker logs agentflow-qdrant-local

# Ver últimas 50 líneas
docker logs --tail 50 agentflow-qdrant-local
```

### Soluciones:

#### Solución 1: Puerto ocupado

**Error en logs**: `bind: address already in use`

```powershell
# Verificar qué está usando el puerto 6333
Get-NetTCPConnection -LocalPort 6333 -ErrorAction SilentlyContinue

# Si hay algo, matar el proceso
$pid = (Get-NetTCPConnection -LocalPort 6333).OwningProcess
Stop-Process -Id $pid -Force

# Reintentar
docker compose -f docker-compose.local.yml restart qdrant-local
```

#### Solución 2: Volumen corrupto

**Error en logs**: `Failed to load collection` o `Storage corrupted`

```powershell
# Detener todo
docker compose -f docker-compose.local.yml down

# Borrar volumen de Qdrant (⚠️ borra datos)
docker volume rm aiagents_qdrant_local_data

# Reiniciar
docker compose -f docker-compose.local.yml up -d
```

#### Solución 3: Imagen corrupta

```powershell
# Eliminar imagen
docker rmi qdrant/qdrant:v1.9.2

# Descargar de nuevo
docker pull qdrant/qdrant:v1.9.2

# Reiniciar
docker compose -f docker-compose.local.yml up -d
```

#### Solución 4: Health check falla (wget no disponible)

Si el health check usa `wget` pero no está en la imagen:

```yaml
# Alternativa en docker-compose.local.yml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:6333/readyz"]
  # O simplemente TCP check
  test: ["CMD-SHELL", "nc -z localhost 6333 || exit 1"]
```

---

## 🔧 Comandos Útiles de Docker

### Ver estado de containers

```powershell
# Todos los containers (running + stopped)
docker ps -a

# Solo los del proyecto AgentFlow
docker ps --filter "name=agentflow"

# Con formato personalizado
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

### Ver logs

```powershell
# Logs de un container específico
docker logs agentflow-qdrant-local

# Seguir logs en tiempo real
docker logs -f agentflow-qdrant-local

# Últimas 100 líneas
docker logs --tail 100 agentflow-qdrant-local
```

### Reiniciar containers

```powershell
# Reiniciar uno específico
docker restart agentflow-qdrant-local

# Reiniciar todos
docker compose -f docker-compose.local.yml restart

# Detener y reiniciar (recrear)
docker compose -f docker-compose.local.yml down
docker compose -f docker-compose.local.yml up -d
```

### Limpiar recursos

```powershell
# Containers detenidos
docker container prune -f

# Imágenes sin usar
docker image prune -f

# Volúmenes huérfanos
docker volume prune -f

# TODO (⚠️ CUIDADO - borra TODO lo no usado)
docker system prune -a --volumes
```

---

## ✅ Health Checks Manuales

### Qdrant

```powershell
# REST API (debe retornar "ok")
curl http://localhost:6333/readyz

# Ver colecciones (debe retornar JSON)
curl http://localhost:6333/collections

# gRPC check (si tienes grpcurl)
grpcurl -plaintext localhost:6334 list
```

### MongoDB

```powershell
# Conexión básica
docker exec -it agentflow-mongo-local mongosh --eval "db.adminCommand('ping')"

# Ver databases
docker exec -it agentflow-mongo-local mongosh --eval "show dbs"
```

### Redis

```powershell
# Ping (debe retornar PONG)
docker exec -it agentflow-redis-local redis-cli ping

# Ver keys
docker exec -it agentflow-redis-local redis-cli keys "*"
```

---

## 🚀 Iniciar Stack Completo (Paso a Paso)

```powershell
# 1. Verificar que Docker Desktop está corriendo
docker info

# 2. Ir al directorio del proyecto
cd c:\labs\aiagents

# 3. Detener cualquier stack anterior
make down-local-full

# 4. Limpiar containers viejos (opcional)
docker compose -f docker-compose.local.yml down

# 5. Iniciar solo infraestructura (sin apps)
docker compose -f docker-compose.local.yml up -d

# 6. Esperar a que todos estén healthy (2-3 minutos)
docker compose -f docker-compose.local.yml ps

# Debe mostrar:
# agentflow-mongo-local    Up (healthy)
# agentflow-redis-local    Up (healthy)
# agentflow-qdrant-local   Up (healthy)
# agentflow-mcp-test       Up (healthy)

# 7. Si todos están healthy, iniciar apps
make up-local-full

# 8. Verificar endpoints
curl http://localhost:5000/health          # API
curl http://localhost:3039                 # Frontend
curl http://localhost:6333/readyz          # Qdrant
```

---

## 🐛 Problemas Comunes

### Error: "no configuration file provided"

```powershell
# Asegúrate de estar en el directorio correcto
cd c:\labs\aiagents

# O especifica la ruta completa
docker compose -f c:\labs\aiagents\docker-compose.local.yml up -d
```

### Error: "Conflict. The container name is already in use"

```powershell
# Detener containers existentes
docker stop agentflow-mongo-local agentflow-redis-local agentflow-qdrant-local agentflow-mcp-test

# Eliminarlos
docker rm agentflow-mongo-local agentflow-redis-local agentflow-qdrant-local agentflow-mcp-test

# Reintentar
docker compose -f docker-compose.local.yml up -d
```

### Error: "port is already allocated"

```powershell
# Ver qué está usando el puerto (ejemplo: 6333)
Get-NetTCPConnection -LocalPort 6333

# Matar proceso
$pid = (Get-NetTCPConnection -LocalPort 6333).OwningProcess
Stop-Process -Id $pid -Force

# Reintentar
docker compose -f docker-compose.local.yml restart qdrant-local
```

### Qdrant se cuelga en "starting"

```powershell
# Ver logs detallados
docker logs -f agentflow-qdrant-local

# Si ves errores de permisos, recrear volumen
docker compose -f docker-compose.local.yml down
docker volume rm aiagents_qdrant_local_data
docker compose -f docker-compose.local.yml up -d
```

---

## 📊 Verificación Completa

Script PowerShell para verificar todo:

```powershell
# verify-docker-stack.ps1

Write-Host "🔍 Verificando Docker Stack..." -ForegroundColor Cyan

# 1. Docker Desktop
Write-Host "`n1. Docker Desktop..." -NoNewline
try {
    docker info > $null 2>&1
    Write-Host " ✅" -ForegroundColor Green
} catch {
    Write-Host " ❌ No está corriendo" -ForegroundColor Red
    exit 1
}

# 2. Containers
Write-Host "2. Containers..." -ForegroundColor Cyan
$containers = @("agentflow-mongo-local", "agentflow-redis-local", "agentflow-qdrant-local", "agentflow-mcp-test")
foreach ($container in $containers) {
    Write-Host "   $container... " -NoNewline
    $status = docker inspect --format='{{.State.Health.Status}}' $container 2>$null
    if ($status -eq "healthy") {
        Write-Host "✅" -ForegroundColor Green
    } else {
        Write-Host "❌ $status" -ForegroundColor Red
    }
}

# 3. Endpoints
Write-Host "`n3. Endpoints..." -ForegroundColor Cyan
$endpoints = @{
    "MongoDB" = "http://localhost:27018"
    "Redis" = "http://localhost:6380"
    "Qdrant" = "http://localhost:6333/readyz"
    "API" = "http://localhost:5000/health"
    "Frontend" = "http://localhost:3039"
}

foreach ($name in $endpoints.Keys) {
    Write-Host "   $name... " -NoNewline
    try {
        $response = Invoke-WebRequest -Uri $endpoints[$name] -Method Get -TimeoutSec 2 -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            Write-Host "✅" -ForegroundColor Green
        } else {
            Write-Host "❌ HTTP $($response.StatusCode)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "❌ No responde" -ForegroundColor Red
    }
}

Write-Host "`n✅ Verificación completada!" -ForegroundColor Green
```

**Ejecutar:**

```powershell
# Guardar script
notepad verify-docker-stack.ps1

# Ejecutar
.\verify-docker-stack.ps1
```

---

## 📚 Referencias

- [Docker Desktop Windows Docs](https://docs.docker.com/desktop/install/windows-install/)
- [Qdrant Documentation](https://qdrant.tech/documentation/)
- [docker-compose.local.yml](../docker-compose.local.yml)
- [INFRASTRUCTURE-SETUP.md](INFRASTRUCTURE-SETUP.md)

---

## 🆘 Soporte

Si ninguna solución funciona:

1. **Captura logs completos**:
   ```powershell
   docker logs agentflow-qdrant-local > qdrant-error.log 2>&1
   ```

2. **Revisa configuración**:
   - `docker-compose.local.yml`
   - Variables de entorno
   - Puertos disponibles

3. **Reinstala Docker Desktop** (último recurso)

---

**Última actualización**: 2025-01-18  
**Stack version**: MongoDB 7, Redis 7, Qdrant 1.9.2
