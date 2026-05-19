# 🐧 Troubleshooting: Docker en Linux

**Guía de solución de problemas para AgentFlow en Linux**

---

## 🔍 Problema: Docker Compose no levanta los containers

### ❌ Síntomas Comunes:

1. **Container Qdrant se queda en "starting"**
   ```bash
   agentflow-qdrant-local   Up (health: starting)
   ```

2. **Health check falla**
   ```
   Unhealthy: Health check failed
   ```

3. **Error: "no such file or directory"**
   ```
   OCI runtime exec failed: exec failed: unable to start container process: 
   exec: "wget": executable file not found in $PATH
   ```

### ✅ Causa Raíz:

**Health checks usaban `wget` pero las imágenes no lo tienen instalado por defecto.**

En la imagen `qdrant/qdrant:v1.9.2` y `node:22-alpine`, `wget` puede no estar disponible.

### ✅ Solución: Usar `curl` o verificación TCP

Ya está **corregido** en el `docker-compose.local.yml`:

```yaml
# Antes (❌ No funcionaba en Linux):
healthcheck:
  test: ["CMD", "wget", "-qO-", "http://localhost:6333/readyz"]

# Ahora (✅ Funciona en Linux):
healthcheck:
  test: ["CMD-SHELL", "curl -f http://localhost:6333/readyz || exit 1"]
  start_period: 10s  # Da tiempo para que el servicio inicie
```

**Reiniciar containers:**

```bash
cd /path/to/aiagents
docker compose -f docker-compose.local.yml down
docker compose -f docker-compose.local.yml up -d
```

---

## 🔧 Problema: Permisos de volúmenes

### ❌ Error:

```
Permission denied: '/data/db'
Permission denied: '/qdrant/storage'
```

### ✅ Solución:

```bash
# 1. Detener containers
docker compose -f docker-compose.local.yml down

# 2. Eliminar volúmenes
docker volume rm aiagents_mongo_local_data
docker volume rm aiagents_redis_local_data
docker volume rm aiagents_qdrant_local_data

# 3. Recrear con permisos correctos
sudo docker compose -f docker-compose.local.yml up -d

# O sin sudo si tu usuario está en el grupo docker:
sudo usermod -aG docker $USER
newgrp docker
docker compose -f docker-compose.local.yml up -d
```

---

## 🔧 Problema: Puerto ya está en uso

### ❌ Error:

```
Error starting userland proxy: listen tcp4 0.0.0.0:6333: bind: address already in use
```

### ✅ Solución:

```bash
# Ver qué proceso está usando el puerto
sudo lsof -i :6333
# O
sudo netstat -tlnp | grep 6333
# O
sudo ss -tlnp | grep 6333

# Matar el proceso
sudo kill -9 <PID>

# Reintentar
docker compose -f docker-compose.local.yml restart qdrant-local
```

---

## 🔧 Problema: Docker daemon no está corriendo

### ❌ Error:

```
Cannot connect to the Docker daemon at unix:///var/run/docker.sock. 
Is the docker daemon running?
```

### ✅ Solución:

**Ubuntu/Debian:**
```bash
sudo systemctl start docker
sudo systemctl enable docker
sudo systemctl status docker
```

**Fedora/RHEL/CentOS:**
```bash
sudo systemctl start docker
sudo systemctl enable docker
```

**Arch Linux:**
```bash
sudo systemctl start docker.service
sudo systemctl enable docker.service
```

**Verificar:**
```bash
docker ps
```

---

## 🔧 Problema: `ss` command not found

### ❌ Error en script:

```bash
bash: ss: command not found
```

### ✅ Solución:

**Instalar `iproute2`:**

```bash
# Ubuntu/Debian
sudo apt-get install iproute2

# Fedora/RHEL/CentOS
sudo yum install iproute

# Arch Linux
sudo pacman -S iproute2
```

**Alternativa**: Modificar `scripts/local-full-up.sh` para usar `netstat`:

```bash
# Cambiar:
if ss -ltn "( sport = :$port )" | grep -q LISTEN; then

# Por:
if netstat -tln | grep -q ":$port "; then
```

---

## 🔧 Problema: MongoDB no arranca

### ❌ Error en logs:

```bash
docker logs agentflow-mongo-local
# Error: mongosh: command not found
```

### ✅ Solución:

La imagen `mongo:7` **incluye** `mongosh`, pero si falla el health check:

```bash
# Opción 1: Cambiar a verificación TCP simple
# Editar docker-compose.local.yml:
healthcheck:
  test: ["CMD-SHELL", "mongosh --eval 'db.adminCommand({ping:1})' --quiet || exit 1"]
  interval: 10s
  timeout: 5s
  retries: 5
  start_period: 30s

# Opción 2: Sin health check (más simple)
# Comentar la sección healthcheck completamente
```

---

## 🔧 Problema: Qdrant no arranca (port binding)

### ❌ Error en logs:

```bash
docker logs agentflow-qdrant-local
# Error: Address already in use (os error 98)
```

### ✅ Solución:

```bash
# Ver si Qdrant está corriendo en otro lugar
ps aux | grep qdrant

# Ver qué está usando los puertos
sudo lsof -i :6333
sudo lsof -i :6334

# Matar procesos
sudo pkill -9 qdrant

# Limpiar y reiniciar
docker compose -f docker-compose.local.yml down
docker compose -f docker-compose.local.yml up -d
```

---

## 📊 Verificación Completa en Linux

### Script de diagnóstico:

```bash
#!/bin/bash
# verify-docker-linux.sh

echo "🔍 Verificando Docker Stack en Linux..."

# 1. Docker daemon
echo -e "\n1. Docker Daemon..."
if systemctl is-active --quiet docker; then
    echo "   ✅ Docker daemon activo"
else
    echo "   ❌ Docker daemon no está corriendo"
    echo "   Solución: sudo systemctl start docker"
    exit 1
fi

# 2. Containers
echo -e "\n2. Containers..."
containers=("agentflow-mongo-local" "agentflow-redis-local" "agentflow-qdrant-local" "agentflow-mcp-test")
for container in "${containers[@]}"; do
    echo -n "   $container... "
    status=$(docker inspect --format='{{.State.Health.Status}}' $container 2>/dev/null)
    if [[ "$status" == "healthy" ]]; then
        echo "✅"
    else
        echo "❌ $status"
    fi
done

# 3. Puertos
echo -e "\n3. Puertos..."
ports=(27018 6380 6333 6334 3501)
for port in "${ports[@]}"; do
    echo -n "   Puerto $port... "
    if ss -tln 2>/dev/null | grep -q ":$port " || netstat -tln 2>/dev/null | grep -q ":$port "; then
        echo "✅ Escuchando"
    else
        echo "❌ No escuchando"
    fi
done

# 4. Endpoints HTTP
echo -e "\n4. Endpoints HTTP..."
endpoints=(
    "http://localhost:6333/readyz|Qdrant"
    "http://localhost:5000/health|API"
    "http://localhost:3039|Frontend"
)

for endpoint_info in "${endpoints[@]}"; do
    IFS='|' read -r url name <<< "$endpoint_info"
    echo -n "   $name... "
    if curl -f -s "$url" > /dev/null 2>&1; then
        echo "✅"
    else
        echo "❌ No responde"
    fi
done

echo -e "\n✅ Verificación completada!"
```

**Ejecutar:**

```bash
chmod +x verify-docker-linux.sh
./verify-docker-linux.sh
```

---

## 🚀 Comandos Útiles Linux

### Iniciar/Detener Docker

```bash
# Iniciar Docker daemon
sudo systemctl start docker

# Detener Docker daemon
sudo systemctl stop docker

# Reiniciar Docker daemon
sudo systemctl restart docker

# Ver status
sudo systemctl status docker

# Ver logs del daemon
sudo journalctl -u docker -f
```

### Gestión de Containers

```bash
# Ver containers corriendo
docker ps

# Ver todos los containers (incluso detenidos)
docker ps -a

# Ver logs de un container
docker logs agentflow-qdrant-local

# Seguir logs en tiempo real
docker logs -f agentflow-qdrant-local

# Últimas 100 líneas
docker logs --tail 100 agentflow-qdrant-local

# Reiniciar container específico
docker restart agentflow-qdrant-local

# Entrar a un container
docker exec -it agentflow-qdrant-local sh
```

### Limpieza de Docker

```bash
# Detener todos los containers
docker stop $(docker ps -aq)

# Eliminar containers detenidos
docker container prune -f

# Eliminar imágenes sin usar
docker image prune -f

# Eliminar volúmenes huérfanos
docker volume prune -f

# Eliminar TODO (⚠️ CUIDADO)
docker system prune -a --volumes
```

### Verificación de Red

```bash
# Ver networks de Docker
docker network ls

# Inspeccionar network
docker network inspect aiagents_default

# Ver qué containers están en una network
docker network inspect aiagents_default -f '{{json .Containers}}' | jq
```

---

## 🔐 Problema: Usuario sin permisos para Docker

### ❌ Error:

```
permission denied while trying to connect to the Docker daemon socket
```

### ✅ Solución:

```bash
# Agregar usuario al grupo docker
sudo usermod -aG docker $USER

# Aplicar cambios sin logout
newgrp docker

# Verificar
docker ps
```

**Si persiste**:

```bash
# Dar permisos al socket (temporal)
sudo chmod 666 /var/run/docker.sock

# Reiniciar Docker (permanente)
sudo systemctl restart docker
```

---

## 📦 Problema: Espacio en disco

### ❌ Error:

```
no space left on device
```

### ✅ Solución:

```bash
# Ver espacio usado por Docker
docker system df

# Limpiar imágenes viejas
docker image prune -a

# Limpiar volúmenes no usados
docker volume prune

# Limpiar todo lo no usado (⚠️ cuidado)
docker system prune -a --volumes

# Ver espacio en disco
df -h
```

---

## 🔧 Problema: DNS no funciona en containers

### ❌ Síntoma:

Containers no pueden resolver nombres de dominio externos.

### ✅ Solución:

**Opción 1: Configurar DNS en daemon.json**

```bash
# Editar /etc/docker/daemon.json
sudo nano /etc/docker/daemon.json

# Agregar:
{
  "dns": ["8.8.8.8", "8.8.4.4"]
}

# Reiniciar Docker
sudo systemctl restart docker
```

**Opción 2: En docker-compose.local.yml**

```yaml
services:
  qdrant-local:
    # ... existing config
    dns:
      - 8.8.8.8
      - 8.8.4.4
```

---

## 📚 Recursos Adicionales

### Logs del Sistema

```bash
# Ver logs del daemon de Docker
sudo journalctl -u docker -f

# Ver logs de systemd
sudo journalctl -xe
```

### Performance

```bash
# Ver stats de containers
docker stats

# Ver uso de recursos por container
docker stats --no-stream
```

### Información del Sistema

```bash
# Info de Docker
docker info

# Versión
docker version

# Plugins instalados
docker plugin ls
```

---

## 🎯 Checklist: Levantar Stack en Linux

```bash
# 1. Verificar Docker daemon
sudo systemctl status docker
# Si no está activo: sudo systemctl start docker

# 2. Verificar permisos
docker ps
# Si falla: sudo usermod -aG docker $USER && newgrp docker

# 3. Ir al proyecto
cd /path/to/aiagents

# 4. Detener stack anterior (si existe)
docker compose -f docker-compose.local.yml down

# 5. Limpiar volúmenes (opcional, borra datos)
# docker volume prune -f

# 6. Iniciar infraestructura
docker compose -f docker-compose.local.yml up -d

# 7. Verificar health
docker compose -f docker-compose.local.yml ps

# 8. Ver logs si algo falla
docker compose -f docker-compose.local.yml logs -f

# 9. Verificar endpoints
curl http://localhost:6333/readyz  # Qdrant
redis-cli -p 6380 ping             # Redis
mongosh --port 27018 --eval "db.adminCommand('ping')"  # MongoDB

# 10. Iniciar apps con Makefile
make up-local-full
```

---

## 🆘 Soporte

Si ninguna solución funciona:

1. **Captura logs completos**:
   ```bash
   docker compose -f docker-compose.local.yml logs > docker-logs.txt
   sudo journalctl -u docker > docker-daemon.log
   ```

2. **Información del sistema**:
   ```bash
   uname -a > system-info.txt
   docker version >> system-info.txt
   docker info >> system-info.txt
   ```

3. **Revisa distribución específica**:
   - Ubuntu/Debian: [Docker Docs Ubuntu](https://docs.docker.com/engine/install/ubuntu/)
   - Fedora: [Docker Docs Fedora](https://docs.docker.com/engine/install/fedora/)
   - Arch: [Arch Wiki Docker](https://wiki.archlinux.org/title/Docker)

---

**Última actualización**: 2025-01-18  
**Plataformas**: Ubuntu 22.04+, Debian 11+, Fedora 38+, Arch Linux
