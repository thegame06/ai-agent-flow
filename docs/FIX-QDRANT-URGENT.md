# 🚨 Fix Urgente: Qdrant Health Check Falla

**Problema**: Container Qdrant no pasa el health check y falla después de 107 segundos.

---

## ⚡ Solución Rápida (Opción 1: Recomendada)

### Usa health check TCP simple

**Ya aplicado en `docker-compose.local.yml`**:

```yaml
qdrant-local:
  healthcheck:
    test: ["CMD-SHELL", "timeout 2 bash -c '</dev/tcp/localhost/6333' || exit 1"]
    interval: 10s
    timeout: 5s
    retries: 10
    start_period: 30s  # ← Más tiempo para iniciar
```

**Reinicia**:

```bash
# Linux/WSL
docker compose -f docker-compose.local.yml down
docker compose -f docker-compose.local.yml up -d

# Espera 30 segundos
sleep 30

# Verifica
docker ps
docker logs agentflow-qdrant-local
```

---

## ⚡ Solución Alternativa (Opción 2: Sin Health Check)

Si el problema persiste, **desactiva** temporalmente el health check:

```yaml
qdrant-local:
  image: qdrant/qdrant:v1.9.2
  container_name: agentflow-qdrant-local
  restart: unless-stopped
  ports:
    - "6333:6333"
    - "6334:6334"
  volumes:
    - qdrant_local_data:/qdrant/storage
  environment:
    - QDRANT__SERVICE__GRPC_PORT=6334
  # Comentar todo el healthcheck temporalmente
  # healthcheck:
  #   test: ...
```

**Luego**:

```bash
docker compose -f docker-compose.local.yml down
docker compose -f docker-compose.local.yml up -d

# Verificar manualmente que responde
curl http://localhost:6333/readyz
# Debe retornar: "ok"
```

---

## ⚡ Solución Alternativa (Opción 3: Más Tiempo)

Incrementa `start_period` y `retries`:

```yaml
healthcheck:
  test: ["CMD-SHELL", "timeout 2 bash -c '</dev/tcp/localhost/6333' || exit 1"]
  interval: 10s
  timeout: 5s
  retries: 20       # ← Aumentado de 10 a 20
  start_period: 60s # ← Aumentado de 30s a 60s
```

---

## 🔍 Diagnóstico

### Ver logs de Qdrant:

```bash
# Últimas 50 líneas
docker logs --tail 50 agentflow-qdrant-local

# Seguir logs en tiempo real
docker logs -f agentflow-qdrant-local
```

### Ver estado del health check:

```bash
docker inspect agentflow-qdrant-local --format='{{json .State.Health}}' | jq
```

### Probar endpoint manualmente:

```bash
# Debe retornar "ok"
curl http://localhost:6333/readyz

# Ver colecciones (vacío la primera vez)
curl http://localhost:6333/collections
```

---

## 🐛 Causas Comunes

### 1. Imagen sin bash/timeout

**Problema**: La imagen de Qdrant no tiene `bash` o `timeout`.

**Solución**: Cambiar a verificación sin comandos externos:

```yaml
healthcheck:
  test: ["CMD-SHELL", "nc -z localhost 6333 || exit 1"]
  # O simplemente desactivar
```

### 2. Puerto no está listo

**Problema**: Qdrant tarda más de 30s en iniciar.

**Solución**: Aumentar `start_period` a 60s o más.

### 3. Volumen corrupto

**Problema**: Datos anteriores causan problemas.

**Solución**:

```bash
docker compose -f docker-compose.local.yml down -v
docker volume rm aiagents_qdrant_local_data
docker compose -f docker-compose.local.yml up -d
```

### 4. Memoria insuficiente

**Problema**: Sistema sin recursos.

**Solución**:

```bash
# Ver uso de recursos
docker stats --no-stream

# Liberar memoria
docker system prune -f
```

---

## 🎯 Script Automático

Ejecuta el script de fix:

```bash
chmod +x scripts/fix-qdrant-health.sh
./scripts/fix-qdrant-health.sh
```

El script:
1. ✅ Detiene containers
2. ✅ Verifica fix aplicado
3. ✅ Inicia solo Qdrant
4. ✅ Espera 30s
5. ✅ Verifica health
6. ✅ Inicia el resto si OK

---

## ✅ Verificación Final

Una vez que funcione:

```bash
# 1. Verifica que Qdrant responde
curl http://localhost:6333/readyz
# Debe retornar: "ok"

# 2. Verifica collections (vacío es normal)
curl http://localhost:6333/collections
# Debe retornar JSON

# 3. Verifica containers
docker ps
# Todos deben estar "Up (healthy)"

# 4. Continua con el stack
make up-local-full
```

---

## 📞 Si Nada Funciona

### Opción Nuclear: Omitir completamente el health check

Edita `docker-compose.local.yml`:

```yaml
qdrant-local:
  image: qdrant/qdrant:v1.9.2
  # ... resto de config ...
  # Eliminar completamente la sección healthcheck

mcp-test:
  # ... resto de config ...
  depends_on:
    mongo-local:
      condition: service_healthy
    redis-local:
      condition: service_healthy
    # Quitar qdrant-local de depends_on
```

**Luego**:

```bash
docker compose -f docker-compose.local.yml down
docker compose -f docker-compose.local.yml up -d

# Esperar manualmente
sleep 30

# Verificar manualmente
curl http://localhost:6333/readyz
```

---

## 📚 Referencias

- [TROUBLESHOOTING-LINUX.md](TROUBLESHOOTING-LINUX.md)
- [Qdrant Docker Docs](https://qdrant.tech/documentation/quick-start/)
- [Docker Health Checks](https://docs.docker.com/engine/reference/builder/#healthcheck)

---

**Última actualización**: 2025-01-18  
**Problema**: Health check falla después de 107s  
**Solución**: TCP check simple o desactivar health check
