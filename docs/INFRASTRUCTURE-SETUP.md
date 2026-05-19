# 🏗️ AgentFlow Infrastructure Setup

**Guía completa de infraestructura para Local, Staging y Producción**

---

## 📋 Tabla de Contenidos

1. [Stack Tecnológico](#-stack-tecnológico)
2. [Desarrollo Local](#-desarrollo-local)
3. [Staging Environment](#-staging-environment)
4. [Production Environment](#-production-environment)
5. [Makefile Commands](#-makefile-commands)
6. [Variables de Entorno](#-variables-de-entorno)
7. [Troubleshooting](#-troubleshooting)

---

## 🔧 Stack Tecnológico

### Infraestructura Base

| Componente | Versión | Puerto | Propósito |
|------------|---------|--------|-----------|
| **MongoDB** | 7.0 | 27018 | Base de datos principal (intents, agents, executions) |
| **Redis** | 7-alpine | 6380 | Distributed locks, hot state, caching |
| **Qdrant** | 1.9.2 | 6333 (REST), 6334 (gRPC) | Vector database para semantic intent matching |
| **MCP Test Server** | Node 22 | 3501 | Model Context Protocol test tools |

### Aplicaciones

| Componente | Stack | Puerto | Propósito |
|------------|-------|--------|-----------|
| **AgentFlow API** | .NET 9 | 5000 | Backend REST API |
| **Frontend** | React 18 + Vite | 3039 | UI (Studio) |
| **WhatsApp QR Bridge** | Node.js | 3401 | WhatsApp channel integration |

---

## 🏠 Desarrollo Local

### 1. Levantar Stack Completo

**Opción A: Todo en uno (Recomendado)**

```bash
make up-local-full
```

Este comando:
1. ✅ Levanta infraestructura Docker (MongoDB, Redis, Qdrant, MCP Test)
2. ✅ Compila y ejecuta AgentFlow API (.NET 9)
3. ✅ Inicia Frontend (React + Vite)
4. ✅ Inicia WhatsApp QR Bridge

**Verificar estado:**

```bash
# Verificar containers
docker ps

# Verificar API
curl http://localhost:5000/health

# Verificar Frontend
curl http://localhost:3039
```

**Opción B: Solo infraestructura**

```bash
# Solo Docker containers
docker compose -f docker-compose.local.yml up -d

# Verificar health
docker compose -f docker-compose.local.yml ps
```

---

### 2. Estructura de docker-compose.local.yml

```yaml
services:
  mongo-local:
    image: mongo:7
    ports: ["27018:27017"]
    volumes: [mongo_local_data:/data/db]
    
  redis-local:
    image: redis:7-alpine
    ports: ["6380:6379"]
    volumes: [redis_local_data:/data]
    
  qdrant-local:
    image: qdrant/qdrant:v1.9.2
    ports: ["6333:6333", "6334:6334"]
    volumes: [qdrant_local_data:/qdrant/storage]
    
  mcp-test:
    image: node:22-alpine
    ports: ["3501:3501"]
    depends_on: [mongo-local, redis-local, qdrant-local]
```

**Puertos Mapeados:**
- **MongoDB**: `27018` → Evita conflicto con MongoDB local (27017)
- **Redis**: `6380` → Evita conflicto con Redis local (6379)
- **Qdrant**: `6333` (REST), `6334` (gRPC)

---

### 3. Comandos Makefile Útiles

#### Iniciar/Detener

```bash
make up-local-full          # Inicia stack completo
make down-local-full        # Detiene stack (preserva datos)
make restart-local-full     # Reinicia stack sin limpiar datos
make clean-local-full       # Mata procesos colgados (preserva volúmenes)
make refresh-local-full     # WIPE COMPLETO + reinicio (⚠️ borra datos)
```

#### Testing

```bash
make test-all               # Tests completos (backend + frontend)
make test-backend           # Solo backend (unit + integration)
make test-unit              # Solo unit tests
make test-integration       # Solo integration tests
make test-frontend          # Lint + build + tests frontend
```

#### QA & Quality

```bash
make qa-one-shot            # Full QA gate (guardrails + tests)
make quality-no-mock        # Valida que no hay mocks en runtime code
make contract-check         # Valida contrato IAgentBrain
```

#### Debugging

```bash
make check-qr CHANNEL_ID=<id>   # Debug WhatsApp QR session
```

---

### 4. Variables de Entorno Locales

**Backend (.NET)**

```bash
# Default en scripts/local-full-up.sh
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://0.0.0.0:5000
ConnectionStrings__MongoDB=mongodb://localhost:27018
ConnectionStrings__Redis=localhost:6380
ConnectionStrings__Qdrant=http://localhost:6333
WhatsApp__QrBridgeApiKey=dev-bridge-key
```

**Frontend (React)**

```bash
# frontend/aiagent_flow/.env.local
VITE_API_BASE_URL=http://localhost:5000
```

**Personalizar puertos:**

```bash
# En tu .bashrc o .zshrc (Linux/Mac)
export API_PORT=5001
export FRONTEND_PORT=3040
export QR_PORT=3402

# En PowerShell (Windows)
$env:API_PORT = 5001
$env:FRONTEND_PORT = 3040

# Luego ejecutar
make up-local-full
```

---

### 5. Logs y PIDs

Ubicaciones:

```
c:\labs\aiagents\.agent/
├── logs/
│   ├── api-full.log          # Logs del API
│   ├── frontend-full.log     # Logs del frontend
│   ├── qr-bridge-full.log    # Logs de WhatsApp bridge
│   └── mcp-server-full.log   # Logs del MCP test server
└── run/
    ├── api.pid               # PID del proceso API
    ├── frontend.pid          # PID del proceso frontend
    ├── qr.pid                # PID del proceso QR bridge
    └── mcp.pid               # PID del proceso MCP
```

**Ver logs en tiempo real:**

```bash
# Windows (PowerShell)
Get-Content -Path .agent/logs/api-full.log -Wait -Tail 50

# Linux/Mac
tail -f .agent/logs/api-full.log
```

---

## 🌐 Staging Environment

### Arquitectura Recomendada

```
┌─────────────────────────────────────────────────────────────┐
│                      Azure / AWS / GCP                      │
├─────────────────────────────────────────────────────────────┤
│  Load Balancer (HTTPS)                                      │
│    ├─ AgentFlow API (Docker/K8s) → 3 replicas              │
│    └─ Frontend (CDN/Static Hosting)                         │
├─────────────────────────────────────────────────────────────┤
│  Data Layer                                                  │
│    ├─ MongoDB Atlas (Cluster M10+) → Staging Tier          │
│    ├─ Redis Enterprise (2GB) → Persistence enabled         │
│    └─ Qdrant Cloud (Starter Plan) → 1M vectors             │
├─────────────────────────────────────────────────────────────┤
│  Observability                                               │
│    ├─ Application Insights / Datadog                        │
│    ├─ Azure Monitor / CloudWatch                            │
│    └─ Sentry (Error Tracking)                               │
└─────────────────────────────────────────────────────────────┘
```

### docker-compose.staging.yml

```yaml
version: '3.8'

services:
  agentflow-api:
    image: ghcr.io/your-org/agentflow-api:staging
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
      - ConnectionStrings__MongoDB=${MONGODB_CONNECTION_STRING}
      - ConnectionStrings__Redis=${REDIS_CONNECTION_STRING}
      - ConnectionStrings__Qdrant=${QDRANT_URL}
      - OpenAI__ApiKey=${OPENAI_API_KEY}
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
      interval: 30s
      timeout: 10s
      retries: 3
    deploy:
      replicas: 3
      resources:
        limits:
          memory: 2G
          cpus: '1.0'

  frontend:
    image: ghcr.io/your-org/agentflow-frontend:staging
    ports:
      - "3039:3039"
    environment:
      - VITE_API_BASE_URL=https://api.staging.agentflow.io
    restart: unless-stopped
```

### CI/CD Pipeline (GitHub Actions)

```yaml
# .github/workflows/deploy-staging.yml
name: Deploy to Staging

on:
  push:
    branches: [develop]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Build Backend
        run: docker build -t agentflow-api:staging -f src/AgentFlow.Api/Dockerfile .
      
      - name: Build Frontend
        run: docker build -t agentflow-frontend:staging -f frontend/aiagent_flow/Dockerfile .
      
      - name: Push to Registry
        run: |
          echo ${{ secrets.GHCR_TOKEN }} | docker login ghcr.io -u ${{ github.actor }} --password-stdin
          docker push ghcr.io/your-org/agentflow-api:staging
          docker push ghcr.io/your-org/agentflow-frontend:staging
      
      - name: Deploy to Azure
        run: |
          az webapp restart --name agentflow-staging --resource-group agentflow-rg
```

---

## 🚀 Production Environment

### Arquitectura High-Availability

```
┌──────────────────────────────────────────────────────────────────────┐
│                    Global Load Balancer (CloudFlare)                 │
├──────────────────────────────────────────────────────────────────────┤
│  Region 1 (US-East)              │  Region 2 (EU-West)               │
│  ├─ K8s Cluster (3 nodes)        │  ├─ K8s Cluster (3 nodes)         │
│  │  ├─ AgentFlow API (6 pods)    │  │  ├─ AgentFlow API (6 pods)     │
│  │  └─ Worker (3 pods)            │  │  └─ Worker (3 pods)             │
│  ├─ MongoDB Atlas (M40 - 3 AZ)   │  ├─ MongoDB Atlas (Read Replica)  │
│  ├─ Redis Enterprise (8GB - 3 AZ)│  ├─ Redis Enterprise (8GB - 3 AZ) │
│  └─ Qdrant Cloud (Enterprise)    │  └─ Qdrant Cloud (Replica)        │
├──────────────────────────────────────────────────────────────────────┤
│  Observability Stack                                                  │
│  ├─ Prometheus + Grafana (Metrics)                                   │
│  ├─ ELK Stack (Logs)                                                  │
│  ├─ Jaeger (Distributed Tracing)                                     │
│  └─ PagerDuty (Alerting)                                              │
├──────────────────────────────────────────────────────────────────────┤
│  Security                                                             │
│  ├─ Azure Key Vault / AWS Secrets Manager                            │
│  ├─ WAF (Web Application Firewall)                                   │
│  └─ DDoS Protection                                                   │
└──────────────────────────────────────────────────────────────────────┘
```

### Kubernetes Deployment (production.yaml)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: agentflow-api
  namespace: production
spec:
  replicas: 6
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 2
      maxUnavailable: 1
  selector:
    matchLabels:
      app: agentflow-api
  template:
    metadata:
      labels:
        app: agentflow-api
        version: v1.0.0
    spec:
      containers:
      - name: api
        image: ghcr.io/your-org/agentflow-api:v1.0.0
        ports:
        - containerPort: 5000
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__MongoDB
          valueFrom:
            secretKeyRef:
              name: agentflow-secrets
              key: mongodb-connection
        resources:
          requests:
            memory: "1Gi"
            cpu: "500m"
          limits:
            memory: "2Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 5000
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 5000
          initialDelaySeconds: 10
          periodSeconds: 5
---
apiVersion: v1
kind: Service
metadata:
  name: agentflow-api-service
  namespace: production
spec:
  type: LoadBalancer
  selector:
    app: agentflow-api
  ports:
  - protocol: TCP
    port: 80
    targetPort: 5000
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: agentflow-api-hpa
  namespace: production
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: agentflow-api
  minReplicas: 6
  maxReplicas: 20
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### Managed Services Recomendados

**MongoDB Atlas (Production)**
- **Tier**: M40+ (Dedicated cluster)
- **Replication**: 3-node replica set (Multi-AZ)
- **Backup**: Continuous backup (Point-in-time restore)
- **Monitoring**: Atlas Performance Advisor + alerts
- **Estimated Cost**: ~$500-1,000/month

**Redis Enterprise Cloud**
- **Plan**: 8GB RAM (Persistence enabled)
- **HA**: Multi-AZ replication
- **Backup**: Daily snapshots
- **Estimated Cost**: ~$150-300/month

**Qdrant Cloud Enterprise**
- **Collections**: 10M+ vectors
- **Replication**: 3 nodes
- **API Requests**: Unlimited
- **Estimated Cost**: ~$500-800/month

**Total Infrastructure Cost**: ~$1,500-2,500/month

---

## 📜 Makefile Commands

### Resumen Completo

```bash
# === Development ===
make up-local-full          # Start full stack (infra + api + frontend + qr bridge)
make down-local-full        # Stop full stack (keep data)
make restart-local-full     # Rebuild API + restart (keep data)
make clean-local-full       # Kill stale processes (keep volumes)
make refresh-local-full     # Full refresh: clean + wipe volumes + start

# === Testing ===
make test-all               # Alias of test-ephemeral (full cycle)
make test-ephemeral         # Full cycle: up -> backend tests -> frontend checks -> down
make test-backend           # Unit + integration backend tests
make test-unit              # Backend unit tests only
make test-integration       # Backend integration tests only
make test-frontend          # Frontend lint/build/test

# === Ephemeral Infra (for CI) ===
make test-env-up            # Start ephemeral Docker infra (mongo/redis/mcp)
make test-env-down          # Stop/remove ephemeral Docker infra

# === Quality Gates ===
make qa-one-shot            # Full QA gate (guardrail + backend + frontend)
make quality-no-mock        # Fail if runtime code contains mock/stub/simulated paths
make contract-check         # Validate IAgentBrain SK/MAF contract golden suite

# === Debugging ===
make check-qr CHANNEL_ID=<id>  # Debug QR bridge/session for a channel

# === Help ===
make help                   # Show all available targets
```

---

## 🔐 Variables de Entorno

### Local Development

```bash
# Backend (.NET) - scripts/local-full-up.sh
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://0.0.0.0:5000
API_PORT=5000
FRONTEND_PORT=3039
QR_PORT=3401
MCP_SERVER_PORT=3502

ConnectionStrings__MongoDB=mongodb://localhost:27018
ConnectionStrings__Redis=localhost:6380
ConnectionStrings__Qdrant=http://localhost:6333

OpenAI__ApiKey=sk-...                        # Optional para local
WhatsApp__QrBridgeApiKey=dev-bridge-key
TENANT_ID=tenant-1
```

### Staging

```bash
ASPNETCORE_ENVIRONMENT=Staging
ConnectionStrings__MongoDB=mongodb+srv://user:pass@staging-cluster.mongodb.net/agentflow
ConnectionStrings__Redis=staging-redis.redis.cache.windows.net:6380,ssl=True,password=...
ConnectionStrings__Qdrant=https://staging.qdrant.cloud:6333

OpenAI__ApiKey=${OPENAI_API_KEY}              # Desde Azure Key Vault
ApplicationInsights__InstrumentationKey=...   # Telemetry
```

### Production

```bash
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__MongoDB=${MONGODB_CONNECTION}    # Desde Secrets Manager
ConnectionStrings__Redis=${REDIS_CONNECTION}
ConnectionStrings__Qdrant=${QDRANT_URL}

OpenAI__ApiKey=${OPENAI_API_KEY}
AzureOpenAI__Endpoint=${AZURE_OPENAI_ENDPOINT}
AzureOpenAI__ApiKey=${AZURE_OPENAI_KEY}

Sentry__Dsn=${SENTRY_DSN}                          # Error tracking
ApplicationInsights__InstrumentationKey=${APPINSIGHTS_KEY}
```

---

## 🐛 Troubleshooting

### 1. Puertos ocupados

**Error**: `Port 5000 is busy. Stop previous stack first: make down-local-full`

```bash
# Windows (PowerShell)
Get-Process -Id (Get-NetTCPConnection -LocalPort 5000).OwningProcess | Stop-Process -Force

# Linux/Mac
lsof -ti:5000 | xargs kill -9
```

### 2. Containers no levantan

**Error**: `agentflow-mongo-local unhealthy`

```bash
# Ver logs
docker logs agentflow-mongo-local

# Reiniciar
docker compose -f docker-compose.local.yml restart mongo-local

# Full wipe (⚠️ borra datos)
docker compose -f docker-compose.local.yml down -v
docker compose -f docker-compose.local.yml up -d
```

### 3. API no compila

**Error**: `❌ [full-up] API build failed. Aborting.`

```bash
# Ver errores detallados
dotnet build src/AgentFlow.Api/AgentFlow.Api.csproj -v detailed

# Limpiar y rebuild
dotnet clean
dotnet restore
dotnet build
```

### 4. Frontend no inicia

**Error**: `Frontend did not listen on port 3039`

```bash
# Ver logs
cat .agent/logs/frontend-full.log

# Verificar node_modules
cd frontend/aiagent_flow
rm -rf node_modules package-lock.json
npm install
npm run dev
```

### 5. Qdrant no conecta

**Error**: `QdrantException: Connection refused`

```bash
# Verificar container
docker ps | grep qdrant

# Ver logs
docker logs agentflow-qdrant-local

# Verificar health
curl http://localhost:6333/readyz

# Recrear si es necesario
docker compose -f docker-compose.local.yml restart qdrant-local
```

### 6. Intent Routing falla

**Error**: `IntentScoringEngine failed: Collection 'intent_vectors' not found`

**Solución**: El Intent Catalog Bootstrap no corrió.

```bash
# Verificar logs del API al iniciar
cat .agent/logs/api-full.log | grep "Intent"

# Debe mostrar:
# [IntentCatalogBootstrap] Loading base intents from YAML...
# [IntentCatalogBootstrap] Loaded 30 base intents
# [VectorIndexer] Indexing 30 intents into Qdrant...
# [VectorIndexer] Collection 'intent_vectors' created
```

Si no aparece:
1. Verificar que Qdrant esté corriendo (`docker ps`)
2. Verificar que `base-intents.yaml` exista en `src/AgentFlow.Intents/Catalog/`
3. Reiniciar API: `make restart-local-full`

### 7. WhatsApp QR no genera

**Error**: `QR bridge exited before port 3401 was ready`

```bash
# Ver logs
cat .agent/logs/qr-bridge-full.log

# Verificar dependencias
cd tools/whatsapp-qr-bridge
npm install

# Probar manualmente
PORT=3401 npm start
```

---

## 📊 Health Checks

### Verificar todo está OK

```bash
# Infra
curl http://localhost:27018     # MongoDB (debe conectar)
redis-cli -p 6380 ping          # Redis (debe retornar PONG)
curl http://localhost:6333/readyz  # Qdrant (debe retornar "ok")

# Apps
curl http://localhost:5000/health        # API (debe retornar 200)
curl http://localhost:3039               # Frontend (debe retornar HTML)
curl http://localhost:3401/health        # QR Bridge (debe retornar 200)
curl http://localhost:3501/health        # MCP Test (debe retornar 200)
```

### Script de Verificación Automática

```bash
# Usar el script incluido
./scripts/test/verify-frontend-backend.ps1   # PowerShell
./scripts/test/verify-frontend-backend.sh    # Bash
```

---

## 🎯 Próximos Pasos

1. **Local Development**: ✅ Listo con `make up-local-full`
2. **Staging**: Crear `docker-compose.staging.yml` y pipeline CI/CD
3. **Production**: Setup de Kubernetes + Managed Services
4. **Observability**: Integrar Prometheus, Grafana, ELK
5. **Security**: Azure Key Vault, WAF, DDoS protection

---

## 📚 Referencias

- [Makefile](../Makefile) - Todos los comandos disponibles
- [docker-compose.local.yml](../docker-compose.local.yml) - Stack local
- [QUICK-START-E2E.md](QUICK-START-E2E.md) - Inicio rápido
- [FRONTEND-BACKEND-INTEGRATION-COMPLETE.md](../frontend/aiagent_flow/FRONTEND-BACKEND-INTEGRATION-COMPLETE.md) - Testing frontend

---

**¿Preguntas?** Consulta el [Troubleshooting](#-troubleshooting) o abre un issue.
