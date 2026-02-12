# Pipeline CI/CD — Jenkins + GitLab

Documentacion de la pipeline de integracion y despliegue continuo. Explica cada stage del `Jenkinsfile`,
como configurar Jenkins y como integrar el repositorio GitLab para disparo automatico.

> **Prerequisito:** La infraestructura del servidor (Docker, K3s, Harbor, Jenkins, SonarQube, MongoDB,
> RabbitMQ, Redis y el stack de observabilidad) debe estar desplegada. Consulta
> [INFRASTRUCTURE.md](INFRASTRUCTURE.md) para la guia completa de instalacion.

---

## Tabla de Contenidos

1. [Vision general](#1-vision-general)
2. [Stages de la pipeline](#2-stages-de-la-pipeline)
3. [Variables de entorno](#3-variables-de-entorno)
4. [Configurar GitLab](#4-configurar-gitlab)
5. [Configurar Jenkins](#5-configurar-jenkins)
6. [Primer despliegue en K3s](#6-primer-despliegue-en-k3s)
7. [Verificacion post-pipeline](#7-verificacion-post-pipeline)
8. [Flujo completo: del push al despliegue](#8-flujo-completo-del-push-al-despliegue)
9. [Troubleshooting](#9-troubleshooting)

---

## 1. Vision general

```
Developer → git push → GitLab → webhook → Jenkins → Pipeline (9 stages)
                                                         │
                            ┌────────────────────────────┘
                            ▼
        ┌─── Checkout ──► Restore & Build ──► Test (xUnit + coverage) ───┐
        │                                                                 │
        │    ┌──── SonarQube Analysis ◄───────────────────────────────────┘
        │    │
        │    └──► Docker Build (3 imagenes) ──► Trivy Scan (CVEs) ───┐
        │                                                             │
        │    ┌──── Push to Harbor ◄───────────────────────────────────┘
        │    │
        │    └──► Deploy to K3s ──► Verify Deployment
        │              │                    │
        │              ▼                    ▼
        │    kubectl apply -R         Health checks:
        │    (app + observabilidad)   API, Frontend,
        │    + rolling update         Worker, Grafana
        └─────────────────────────────────────────────
```

La pipeline se ejecuta automaticamente con cada push a `main` en GitLab, o manualmente desde Jenkins.

---

## 2. Stages de la pipeline

### Stage 1: Checkout

```groovy
stage('Checkout') {
    steps {
        checkout scm
    }
}
```

Clona el repositorio desde GitLab usando las credenciales configuradas en el job.

---

### Stage 2: Restore & Build

```groovy
dotnet restore
dotnet build --configuration Release --no-restore
```

Restaura los paquetes NuGet y compila toda la solucion (`Project.slnx`) en modo Release.
Incluye los proyectos: Api, Worker, Domain, Application, Infrastructure y los tests.

---

### Stage 3: Test

```groovy
dotnet test --configuration Release --no-build \
    --logger "trx;LogFileName=results.trx" \
    --collect:"XPlat Code Coverage" \
    -- DataCollectors.DataCollector.Configuration.Format=opencover
```

Ejecuta los tests unitarios (Domain + Application) y genera:
- **results.trx**: Resultados en formato Visual Studio Test Results.
- **coverage.opencover.xml**: Reporte de cobertura de codigo en formato OpenCover.

Estos ficheros se usan en el siguiente stage por SonarQube.

**Tests incluidos:**
- `Project.Domain.Tests` — Entidades, value objects, reglas de dominio.
- `Project.Application.Tests` — Handlers, logica de negocio con mocks.
- `Project.Integration.Tests` — Endpoints HTTP (requiere infraestructura levantada).

---

### Stage 4: SonarQube Analysis

```groovy
dotnet sonarscanner begin /k:"project" /d:sonar.host.url="${SONAR_HOST}" ...
dotnet build --configuration Release --no-restore
dotnet sonarscanner end ...
```

Analisis estatico de calidad de codigo:
- **Code smells**: Codigo que funciona pero es dificil de mantener.
- **Bugs**: Patrones de codigo que probablemente causen errores.
- **Vulnerabilidades**: Problemas de seguridad en el codigo fuente.
- **Cobertura**: Porcentaje de codigo cubierto por tests (del report OpenCover).
- **Duplicaciones**: Codigo duplicado que deberia refactorizarse.

Accede a los resultados en `http://server:9000` → proyecto `project`.

---

### Stage 5: Docker Build

```groovy
docker build -t server:8085/project/api:${IMAGE_TAG}     -f src/Project.Api/Dockerfile .
docker build -t server:8085/project/worker:${IMAGE_TAG}   -f src/Project.Worker/Dockerfile .
docker build -t server:8085/project/frontend:${IMAGE_TAG} -f frontend/Dockerfile frontend/
```

Construye 3 imagenes Docker con builds multi-stage:

| Imagen | Base | Peso aprox. |
|--------|------|-------------|
| `project/api` | .NET 10 ASP.NET Runtime | ~120 MB |
| `project/worker` | .NET 10 ASP.NET Runtime | ~110 MB |
| `project/frontend` | Nginx Alpine | ~25 MB |

Cada imagen se tagea con el numero de build de Jenkins (`BUILD_NUMBER`).

---

### Stage 6: Trivy Security Scan

```groovy
trivy image --exit-code 0 --severity CRITICAL,HIGH --format table \
    server:8085/project/api:${IMAGE_TAG}
```

Escanea cada imagen Docker en busca de vulnerabilidades conocidas (CVEs):
- **CRITICAL**: Vulnerabilidades que permiten ejecucion remota de codigo o escalada de privilegios.
- **HIGH**: Vulnerabilidades graves que requieren atencion.

El `--exit-code 0` hace que el pipeline continue incluso con vulnerabilidades (informativo).
Para bloquear el despliegue ante CVEs criticas, cambia a `--exit-code 1`.

> Trivy tambien esta integrado en Harbor, que escanea las imagenes al recibirlas.

---

### Stage 7: Push to Harbor

```groovy
docker login server:8085
docker push server:8085/project/api:${IMAGE_TAG}
docker push server:8085/project/worker:${IMAGE_TAG}
docker push server:8085/project/frontend:${IMAGE_TAG}
```

Sube las 3 imagenes al registro privado Harbor con dos tags cada una:
- `:{BUILD_NUMBER}` — Tag inmutable para trazabilidad.
- `:latest` — Tag movil que apunta siempre a la ultima build.

Verifica las imagenes en `http://server:8085` → proyecto `project`.

---

### Stage 8: Deploy to K3s

```groovy
kubectl apply -R -f k8s/
kubectl set image deployment/api     api=server:8085/project/api:${IMAGE_TAG}     -n project
kubectl set image deployment/worker  worker=server:8085/project/worker:${IMAGE_TAG}  -n project
kubectl set image deployment/frontend frontend=server:8085/project/frontend:${IMAGE_TAG} -n project
kubectl rollout status deployment/api -n project --timeout=120s
```

Este stage hace dos cosas:

1. **`kubectl apply -R -f k8s/`**: Aplica **recursivamente** todos los manifiestos YAML,
   incluyendo los de `k8s/observability/`. Esto actualiza:
   - Namespace, ConfigMap, Secrets
   - Deployments de API (2 replicas), Worker (1 replica), Frontend (4 replicas)
   - Redis, Ingress
   - OTel Collector, Prometheus, AlertManager, Loki, Tempo, Grafana, FluentBit

2. **`kubectl set image`**: Actualiza la imagen de cada deployment al tag del build actual,
   provocando un **rolling update** sin downtime.

3. **`kubectl rollout status`**: Espera a que el rolling update complete con exito
   (timeout: 120s para API/Worker, 60s para frontend).

---

### Stage 9: Verify Deployment

```groovy
# Health check API (con reintentos)
curl http://${NODE_IP}:30080/health  → HTTP 200

# Health check Frontend
curl http://${NODE_IP}:30081/        → HTTP 200

# Health check Grafana (no bloqueante)
curl http://${NODE_IP}:30030/api/health → HTTP 200

# Worker readiness
kubectl get deployment worker -n project → readyReplicas >= 1
```

Verifica que el despliegue ha sido exitoso:
- **API**: 5 reintentos con 10s de espera. Si falla, el pipeline falla.
- **Frontend**: Una comprobacion. Si falla, el pipeline falla.
- **Grafana**: Comprobacion informativa (WARNING si falla, no bloquea el pipeline).
- **Worker**: Verifica que al menos 1 replica esta lista via la API de Kubernetes.

---

### Post: Cleanup

```groovy
post {
    always {
        docker rmi server:8085/project/api:${IMAGE_TAG} || true
        docker rmi server:8085/project/worker:${IMAGE_TAG} || true
        docker rmi server:8085/project/frontend:${IMAGE_TAG} || true
    }
}
```

Limpia las imagenes Docker locales del build para no llenar el disco del servidor Jenkins.

---

## 3. Variables de entorno

Variables definidas en el bloque `environment` del Jenkinsfile:

| Variable | Valor | Descripcion |
|----------|-------|-------------|
| `HARBOR_REGISTRY` | `server:8085` | Host y puerto del registro Harbor |
| `HARBOR_PROJECT` | `project` | Nombre del proyecto en Harbor |
| `SONAR_HOST` | `http://server:9000` | URL de SonarQube |
| `IMAGE_TAG` | `${BUILD_NUMBER}` | Tag de la imagen = numero de build |
| `DOTNET_CLI_TELEMETRY_OPTOUT` | `1` | Desactiva telemetria de .NET CLI |

---

## 4. Configurar GitLab

### 4.1. Crear repositorio

1. Accede a tu instancia de GitLab (ejemplo: `http://server:8081`).
2. Crea un nuevo proyecto con el nombre que desees.
3. Sube el codigo:

```bash
cd /ruta/al/proyecto
git remote add origin http://server:8081/TU_USUARIO/project.git
git push -u origin main
```

### 4.2. Configurar webhook para Jenkins

1. En GitLab: **Settings → Webhooks**.
2. URL: `http://server:8080/project/project-pipeline` (ajusta al nombre de tu job en Jenkins).
3. Trigger: **Push events** → Branch filter: `main`.
4. Desmarca **Enable SSL verification** (home lab con HTTP).
5. Guarda y haz clic en **Test → Push events** para verificar conectividad.

> **Nota:** El formato de la URL del webhook es: `http://JENKINS_HOST:JENKINS_PORT/project/NOMBRE_DEL_JOB`

### 4.3. Credenciales de GitLab en Jenkins

Si el repositorio es privado, necesitas credenciales en Jenkins para clonarlo:

1. En Jenkins: **Manage Jenkins → Credentials → Global**.
2. Añade un **Username/Password** con tu usuario y password/token de GitLab.
3. Usa ese credential ID al configurar el pipeline (paso 5.3).

---

## 5. Configurar Jenkins

### 5.1. Plugins necesarios

Instala desde **Manage Jenkins → Plugins → Available**:
- **Git** (suele venir preinstalado)
- **Pipeline** (suele venir preinstalado)
- **GitLab** (para webhooks)

### 5.2. Credenciales

Configura en **Manage Jenkins → Credentials → System → Global credentials**:

| ID | Tipo | Descripcion |
|----|------|-------------|
| `harbor-creds` | Username/Password | Usuario y password de Harbor (admin) |
| `sonar-token` | Secret text | Token de analisis de SonarQube |
| `gitlab-creds` | Username/Password | Usuario y password/token de GitLab (si repo privado) |

### 5.3. Crear el pipeline

1. **New Item** → nombre: `project-pipeline` → tipo: **Pipeline**.
2. En **Build Triggers**: marca **Build when a change is pushed to GitLab**.
   - Copia la URL del webhook que muestra (la necesitas en el paso 4.2).
3. En **Pipeline**:
   - Definition: **Pipeline script from SCM**.
   - SCM: **Git**.
   - Repository URL: `http://server:8081/TU_USUARIO/project.git`.
   - Credentials: las de GitLab.
   - Branch: `*/main`.
   - Script Path: `Jenkinsfile`.
4. Guarda.

### 5.4. Verificar herramientas

Jenkins necesita estas herramientas instaladas en el servidor (ver [INFRASTRUCTURE.md](INFRASTRUCTURE.md) paso 6):

```bash
# Verificar desde el servidor
dotnet --version                    # .NET 10.x
docker version                      # Docker Engine
trivy --version                     # Trivy scanner
kubectl version --client            # kubectl (via K3s)
dotnet sonarscanner --version       # SonarScanner (como usuario jenkins)
```

---

## 6. Primer despliegue en K3s

Antes de que la pipeline pueda desplegar, K3s necesita preparacion inicial (una sola vez):

### 6.1. Namespace y configuracion base

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/configmap.yaml
```

### 6.2. Secrets con valores reales

Edita `k8s/secrets.yaml` con las connection strings de tu servidor y aplica:

```bash
# Editar antes de aplicar
nano k8s/secrets.yaml
kubectl apply -f k8s/secrets.yaml
```

Valores a configurar:

| Secret | Ejemplo |
|--------|---------|
| `mongodb-connection` | `mongodb://admin:TU_PASSWORD@192.168.1.100:27017` |
| `rabbitmq-connection` | `amqp://admin:TU_PASSWORD@192.168.1.100:5672` |
| `redis-connection` | `192.168.1.100:6379` |
| `jwt-secret` | Un string aleatorio de minimo 32 caracteres |
| `sentry-dsn` | DSN de Sentry (o vacio si no se usa) |

> Las connection strings usan la IP del host porque MongoDB, RabbitMQ y Redis corren como
> contenedores Docker fuera de K3s. Ver [INFRASTRUCTURE.md](INFRASTRUCTURE.md) para detalles.

### 6.3. Secret para pull de imagenes

K3s necesita autenticarse contra Harbor para descargar imagenes:

```bash
kubectl create secret docker-registry harbor-registry \
  --docker-server=server:8085 \
  --docker-username=admin \
  --docker-password=TU_PASSWORD_HARBOR \
  -n project
```

### 6.4. Stack de observabilidad

La pipeline aplica los manifiestos automaticamente, pero en el primer despliegue
conviene aplicarlos manualmente para verificar que todo arranca:

```bash
kubectl apply -R -f k8s/observability/
kubectl get pods -n project
```

### 6.5. Lanzar la pipeline

Con todo configurado, lanza la primera build:
- **Opcion A**: Haz `git push` a `main` → el webhook dispara Jenkins automaticamente.
- **Opcion B**: En Jenkins → `project-pipeline` → **Build Now**.

---

## 7. Verificacion post-pipeline

Tras una ejecucion exitosa de la pipeline, verifica:

### Servicios de la aplicacion

```bash
# API health check
curl http://server:30080/health
# Healthy

# Frontend
curl -s -o /dev/null -w "%{http_code}" http://server:30081/
# 200

# API docs (Scalar)
# http://server:30080/scalar/v1  (solo en Development/Staging)
```

### Pods en K3s

```bash
kubectl get pods -n project
```

Resultado esperado:

```
NAME                              READY   STATUS    RESTARTS
api-xxxxx-yyy                     1/1     Running   0          ← x2 replicas
api-xxxxx-zzz                     1/1     Running   0
worker-xxxxx-yyy                  1/1     Running   0          ← x1 replica
frontend-xxxxx-aaa                1/1     Running   0          ← x4 replicas
frontend-xxxxx-bbb                1/1     Running   0
frontend-xxxxx-ccc                1/1     Running   0
frontend-xxxxx-ddd                1/1     Running   0
redis-xxxxx-yyy                   1/1     Running   0
otel-collector-xxxxx              1/1     Running   0
prometheus-xxxxx                  1/1     Running   0
alertmanager-xxxxx                1/1     Running   0
loki-xxxxx                        1/1     Running   0
tempo-xxxxx                       1/1     Running   0
grafana-xxxxx                     1/1     Running   0
fluentbit-xxxxx                   1/1     Running   0
```

### Consolas web

| Servicio | URL | Credenciales |
|----------|-----|--------------|
| Jenkins | `http://server:8080` | Las configuradas en la instalacion |
| GitLab | `http://server:8081` | Tu usuario |
| Harbor | `http://server:8085` | admin / tu password |
| SonarQube | `http://server:9000` | admin / tu password |
| RabbitMQ | `http://server:15672` | admin / tu password |
| Grafana | `http://server:30030` | admin / tu password (configurada en grafana.yaml) |

### RabbitMQ — Colas MassTransit

Tras el primer arranque del Worker, MassTransit crea automaticamente en RabbitMQ:

- **`email_queue`**: Cola principal (Quorum Queue) para mensajes de email.
- **`email_queue_error`**: Cola de errores (Dead Letter) para mensajes que fallan tras 3 reintentos.
- **`email_queue_skipped`**: Cola para mensajes descartados.

Verifica en el panel de RabbitMQ (`http://server:15672`) que las colas existen y tienen tipo `quorum`.

---

## 8. Flujo completo: del push al despliegue

```
1. Developer hace git push a main en GitLab

2. GitLab envia webhook POST a Jenkins
       │
3. Jenkins inicia pipeline
       │
4. Checkout → clona el repo
       │
5. Restore & Build → compila en Release
       │
6. Test → ejecuta xUnit (Domain + Application)
       │           genera coverage OpenCover
       │
7. SonarQube → analiza calidad + cobertura
       │           resultados en http://server:9000
       │
8. Docker Build → 3 imagenes multi-stage
       │           api, worker, frontend
       │
9. Trivy Scan → escanea CVEs (CRITICAL/HIGH)
       │           informativo (no bloquea)
       │
10. Push to Harbor → sube 3 imagenes con 2 tags cada una
       │              :BUILD_NUMBER + :latest
       │              verificable en http://server:8085
       │
11. Deploy to K3s → kubectl apply -R (app + observabilidad)
       │             rolling update de api, worker, frontend
       │             espera rollout completo
       │
12. Verify → health checks HTTP + readiness del Worker
       │
13. Cleanup → elimina imagenes locales

       ▼
    Pipeline OK / FAILED
```

---

## 9. Troubleshooting

### La pipeline falla en Checkout

**Error:** `Failed to connect to repository`

- Verifica que Jenkins tiene credenciales de GitLab configuradas.
- Verifica conectividad: `curl http://server:8081` desde el servidor Jenkins.

### La pipeline falla en Test

- Los tests de `Integration.Tests` requieren MongoDB y RabbitMQ corriendo.
  Si solo quieres tests unitarios, filtra: `dotnet test --filter "Category!=Integration"`.

### La pipeline falla en SonarQube

**Error:** `sonarscanner: command not found`

```bash
# Instalar como usuario jenkins
sudo -u jenkins bash -c 'dotnet tool install --global dotnet-sonarscanner'
```

Asegurate de que el PATH incluye `~/.dotnet/tools` (ver Jenkinsfile linea 43).

### La pipeline falla en Docker Build

**Error:** `Cannot connect to the Docker daemon`

```bash
sudo usermod -aG docker jenkins
sudo systemctl restart jenkins
```

### La pipeline falla en Push to Harbor

**Error:** `unauthorized: authentication required`

- Verifica la credencial `harbor-creds` en Jenkins.
- Verifica que el proyecto `project` existe en Harbor.
- Verifica que Docker confie en el registro: `/etc/docker/daemon.json` → `insecure-registries`.

### La pipeline falla en Deploy to K3s

**Error:** `The connection to the server was refused`

- Verifica kubeconfig: `ls -la /var/lib/jenkins/.kube/config`
- Verifica permisos: el archivo debe ser propiedad de `jenkins:jenkins`.
- Verifica K3s: `sudo systemctl status k3s`

**Error:** `ImagePullBackOff` en los pods

- K3s no puede descargar de Harbor. Verifica `/etc/rancher/k3s/registries.yaml`.
- Verifica el secret `harbor-registry` en el namespace `project`.

### La pipeline falla en Verify

**Error:** `API health check FAILED after 5 attempts`

Revisa los logs del pod:

```bash
kubectl logs deployment/api -n project
```

Causas comunes:
- MongoDB/RabbitMQ/Redis no accesibles (connection string incorrecta en secrets).
- JWT Secret demasiado corto (minimo 32 caracteres).
- Puerto 30080 ocupado por otro servicio.
