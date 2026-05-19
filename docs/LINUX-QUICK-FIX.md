# 🚀 Quick Fix: Docker en Linux

**Guía rápida para resolver problemas de Docker en Linux (2 minutos)**

---

## ✅ Problema Resuelto: Health Checks

El `docker-compose.local.yml` ha sido **corregido** para Linux.

### ❌ Antes (No funcionaba):
```yaml
healthcheck:
  test: ["CMD", "wget", "-qO-", "http://localhost:6333/readyz"]
```
**Problema**: `wget` no está en las imágenes de Docker.

### ✅ Ahora (Funciona):
```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -f http://localhost:6333/readyz || exit 1"]
  start_period: 10s  # Da tiempo para iniciar
```
**Solución**: Usa `curl` con `CMD-SHELL` que sí está disponible.

---

## 🔧 Pasos para Levantar en Linux

### 1. Verificar Docker está corriendo

```bash
# Iniciar Docker daemon
sudo systemctl start docker

# Verificar
docker ps
```

**Si falla con "permission denied"**:
```bash
sudo usermod -aG docker $USER
newgrp docker
```

### 2. Limpiar stack anterior

```bash
cd /path/to/aiagents

# Detener containers
docker compose -f docker-compose.local.yml down

# (Opcional) Limpiar volúmenes si tienes problemas
docker volume prune -f
```

### 3. Iniciar containers

```bash
# Levantar infraestructura
docker compose -f docker-compose.local.yml up -d

# Esperar 30 segundos
sleep 30

# Verificar estado
docker compose -f docker-compose.local.yml ps
```

**Resultado esperado**:
```
NAME                       STATUS
agentflow-mongo-local      Up (healthy)
agentflow-redis-local      Up (healthy)
agentflow-qdrant-local     Up (healthy)
agentflow-mcp-test         Up (healthy)
```

### 4. Ejecutar script de verificación

```bash
# Dar permisos
chmod +x scripts/verify-docker-linux.sh

# Ejecutar
./scripts/verify-docker-linux.sh
```

**Debe mostrar**:
```
✅ Docker daemon activo
✅ agentflow-mongo-local... Healthy
✅ agentflow-redis-local... Healthy
✅ agentflow-qdrant-local... Healthy
✅ agentflow-mcp-test... Healthy
✅ MongoDB (puerto 27018)... Escuchando
✅ Redis (puerto 6380)... Escuchando
✅ Qdrant REST (puerto 6333)... Escuchando
✅ Qdrant (http://localhost:6333/readyz)... Responde
```

### 5. Iniciar aplicaciones

```bash
make up-local-full
```

**O manualmente**:
```bash
# Terminal 1: API
cd src/AgentFlow.Api
dotnet run

# Terminal 2: Frontend
cd frontend/aiagent_flow
npm run dev
```

---

## 🐛 Problemas Comunes

### Container se queda en "starting"

```bash
# Ver logs
docker logs agentflow-qdrant-local

# Si health check falla, reiniciar
docker restart agentflow-qdrant-local

# Esperar 10 segundos
sleep 10

# Verificar de nuevo
docker ps
```

### Puerto ya está en uso

```bash
# Ver qué está usando el puerto 6333
sudo lsof -i :6333

# Matar proceso
sudo kill -9 <PID>

# Reiniciar container
docker restart agentflow-qdrant-local
```

### Container "unhealthy"

```bash
# Ver logs detallados
docker logs --tail 100 agentflow-qdrant-local

# Recrear container
docker compose -f docker-compose.local.yml up -d --force-recreate qdrant-local
```

---

## ✅ Verificación Manual

```bash
# Qdrant
curl http://localhost:6333/readyz
# Debe retornar: "ok"

# MongoDB
mongosh --port 27018 --eval "db.adminCommand('ping')"
# Debe retornar: { ok: 1 }

# Redis
redis-cli -p 6380 ping
# Debe retornar: PONG
```

---

## 📚 Documentación Completa

- **[TROUBLESHOOTING-LINUX.md](TROUBLESHOOTING-LINUX.md)** ← Guía completa con todos los casos
- **[INFRASTRUCTURE-SETUP.md](INFRASTRUCTURE-SETUP.md)** ← Setup local/staging/producción

---

## 🆘 Si Nada Funciona

```bash
# Full reset (⚠️ borra todos los datos)
docker compose -f docker-compose.local.yml down -v
docker system prune -a --volumes
docker compose -f docker-compose.local.yml up -d
```

Luego ejecuta: `./scripts/verify-docker-linux.sh`

---

**Última actualización**: 2025-01-18  
**Problema resuelto**: Health checks con `wget` → Cambiado a `curl`
