# Project Template — Instrucciones para Claude

Este repositorio es una plantilla de arquitectura cloud-native .NET 10 con CI/CD completo.
Tú (Claude) eres responsable de que toda la infraestructura funcione. El humano solo se
preocupa de las reglas de negocio.

---

## Contexto del proyecto

- **Arquitectura**: Hexagonal (Ports & Adapters) + DDD + CQRS + Event-Driven
- **Backend**: .NET 10, ASP.NET Core Minimal APIs, MediatR, FluentValidation
- **Mensajería**: MassTransit 8.4 sobre RabbitMQ (Quorum Queues, Outbox pattern MongoDB)
- **Base de datos**: MongoDB 7
- **Caché**: Redis 7
- **Auth**: JWT Bearer + Refresh Tokens + BCrypt
- **Observabilidad**: Serilog, OpenTelemetry → OTel Collector → Prometheus/Tempo, FluentBit → Loki, Grafana, Sentry
- **CI/CD**: Jenkins (9 stages), Docker multi-stage, Trivy, SonarQube, Harbor
- **Orquestación**: K3s con Nginx Ingress Controller
- **Frontend**: Ionic/Angular + Nginx (4 réplicas)
- **Documentación**: README.md (visión global), README_PIPELINE.md (pipeline), INFRASTRUCTURE.md (guía manual)

### Estructura de archivos clave

```
Jenkinsfile                          # Pipeline CI/CD (9 stages)
docker-compose.yml                   # MongoDB + RabbitMQ + Redis (dev local)
k8s/                                 # Manifiestos Kubernetes
k8s/namespace.yaml                   # Namespace: project
k8s/configmap.yaml                   # Variables de entorno no sensibles
k8s/secrets.yaml                     # Connection strings (PLANTILLA — hay que reemplazar)
k8s/api-deployment.yaml              # API: 2 réplicas, NodePort 30080
k8s/worker-deployment.yaml           # Worker: 1 réplica
k8s/frontend-deployment.yaml         # Frontend: 4 réplicas, NodePort 30081
k8s/redis-deployment.yaml            # Redis dentro de K3s
k8s/ingress.yaml                     # Nginx Ingress + TLS
k8s/observability/                   # OTel Collector, Prometheus, Loki, Tempo, Grafana, FluentBit
src/Project.Api/                     # API HTTP
src/Project.Worker/                  # Consumer MassTransit
src/Project.Domain/                  # Entidades, Ports
src/Project.Application/             # Handlers, DTOs, Validators
src/Project.Infrastructure/          # MongoDB, MassTransit, Redis, JWT, Polly
frontend/                            # Ionic/Angular
```

---

## PLAYBOOK DE DESPLIEGUE AUTOMATIZADO

Cuando el humano te pida desplegar la infraestructura, verificarla, o repararla, sigue
este playbook paso a paso. Cada paso es **idempotente**: comprueba si ya existe antes de actuar.

### Requisitos previos

Antes de empezar, necesitas obtener del humano:

1. **IP del servidor** (la IP fija del servidor Ubuntu en la red local)
2. **Contraseña de administrador** para los servicios (una sola contraseña maestra que usarás para Harbor, GitLab, SonarQube, MongoDB, RabbitMQ, Grafana, y como base para el JWT secret)
3. **Confirmación de permisos de root** (necesitas sudo sin restricciones)

Pide estos 3 datos con AskUserQuestion antes de empezar. Almacénalos mentalmente como:
- `SERVER_IP` (ejemplo: 192.168.1.100)
- `ADMIN_PASSWORD` (ejemplo: MiPasswordSegura123!)
- El JWT secret será: `{ADMIN_PASSWORD}_JWT_SECRET_32_CHARS!!` (asegúrate de que tenga 32+ caracteres)

### Importante: ejecución

- Ejecuta los comandos con `sudo` cuando sea necesario (pide permiso al humano la primera vez).
- Cada sección tiene un bloque CHECK y un bloque FIX. Ejecuta CHECK primero. Si pasa, salta al siguiente. Si no, ejecuta FIX.
- Si un paso falla, NO sigas adelante. Diagnostica, arregla, y re-verifica antes de continuar.
- Usa `set -e` en scripts multi-línea para fallar rápido.

---

### PASO 0: Verificar sistema operativo y acceso

**CHECK:**
```bash
lsb_release -d  # Debe ser Ubuntu 22.04/24.04
whoami           # Tu usuario actual
sudo whoami      # Debe responder "root"
ip -4 addr show | grep "inet " | grep -v 127.0.0.1  # Confirmar IP
```

**FIX:** Si sudo no funciona, pide al humano que ejecute `sudo usermod -aG sudo $USER`.

---

### PASO 1: Paquetes base del sistema

**CHECK:**
```bash
which curl wget git docker jq htop > /dev/null 2>&1 && echo "OK" || echo "FALTAN_PAQUETES"
```

**FIX:**
```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y curl wget git unzip apt-transport-https ca-certificates gnupg lsb-release net-tools htop jq
```

---

### PASO 2: Hostname e IP fija

**CHECK:**
```bash
hostname                    # Debe ser "server"
cat /etc/hosts | grep "server"   # Debe tener la línea con SERVER_IP
```

**FIX:**
```bash
sudo hostnamectl set-hostname server
echo "{SERVER_IP} server" | sudo tee -a /etc/hosts
```

Para IP fija con netplan (solo si la IP actual no es fija):
```bash
# Identificar interfaz
ip link show | grep "state UP" | awk -F: '{print $2}' | tr -d ' '
# Crear config netplan (ajustar interfaz y gateway)
```

---

### PASO 3: Docker Engine

**CHECK:**
```bash
docker version > /dev/null 2>&1 && echo "OK" || echo "NO_DOCKER"
docker compose version > /dev/null 2>&1 && echo "OK" || echo "NO_COMPOSE"
cat /etc/docker/daemon.json 2>/dev/null | jq '.["insecure-registries"]' | grep "server:8085" > /dev/null 2>&1 && echo "OK" || echo "NO_INSECURE_REGISTRY"
```

**FIX:**
```bash
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker $USER
```

Configurar registro inseguro para Harbor:
```bash
sudo mkdir -p /etc/docker
echo '{"insecure-registries":["server:8085"],"log-driver":"json-file","log-opts":{"max-size":"10m","max-file":"3"}}' | sudo tee /etc/docker/daemon.json
sudo systemctl restart docker
```

---

### PASO 4: K3s (Kubernetes)

**CHECK:**
```bash
kubectl get nodes 2>/dev/null | grep " Ready " && echo "OK" || echo "NO_K3S"
cat /etc/rancher/k3s/registries.yaml 2>/dev/null | grep "server:8085" > /dev/null 2>&1 && echo "OK" || echo "NO_K3S_REGISTRY"
```

**FIX:**
```bash
curl -sfL https://get.k3s.io | sh -s - --write-kubeconfig-mode 644 --disable traefik --docker
mkdir -p ~/.kube
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $USER:$USER ~/.kube/config
chmod 600 ~/.kube/config
```

Configurar K3s para Harbor:
```bash
sudo mkdir -p /etc/rancher/k3s
echo 'mirrors:
  "server:8085":
    endpoint:
      - "http://server:8085"' | sudo tee /etc/rancher/k3s/registries.yaml
sudo systemctl restart k3s
```

Instalar Nginx Ingress Controller:
```bash
kubectl get ns ingress-nginx > /dev/null 2>&1 || kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/cloud/deploy.yaml
```

---

### PASO 5: Harbor (registro de contenedores) — Puerto 8085

**CHECK:**
```bash
curl -s -o /dev/null -w "%{http_code}" http://server:8085/api/v2.0/health 2>/dev/null | grep -q "200" && echo "OK" || echo "NO_HARBOR"
```

**FIX:**
```bash
cd /opt
sudo mkdir -p harbor && sudo chown $USER:$USER harbor
cd harbor
HARBOR_VERSION="v2.11.0"
wget -q https://github.com/goharbor/harbor/releases/download/${HARBOR_VERSION}/harbor-offline-installer-${HARBOR_VERSION}.tgz
tar xzf harbor-offline-installer-${HARBOR_VERSION}.tgz
cd harbor
cp harbor.yml.tmpl harbor.yml
```

Editar harbor.yml — Reemplazar estos valores:
- `hostname: server`
- `http.port: 8085`
- Comentar toda la sección `https:`
- `harbor_admin_password: {ADMIN_PASSWORD}`
- `data_volume: /data/harbor`

```bash
sudo mkdir -p /data/harbor
sudo ./install.sh --with-trivy
```

Crear servicio systemd:
```bash
sudo tee /etc/systemd/system/harbor.service <<EOF
[Unit]
Description=Harbor Container Registry
After=docker.service
Requires=docker.service
[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=/opt/harbor/harbor
ExecStart=/usr/bin/docker compose up -d
ExecStop=/usr/bin/docker compose down
[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload
sudo systemctl enable harbor
```

Crear proyecto en Harbor:
```bash
curl -s -u "admin:{ADMIN_PASSWORD}" -X POST "http://server:8085/api/v2.0/projects" \
  -H "Content-Type: application/json" \
  -d '{"project_name":"project","public":true}' || true
```

---

### PASO 6: GitLab CE — Puerto 8081

**CHECK:**
```bash
curl -s -o /dev/null -w "%{http_code}" http://server:8081/-/health 2>/dev/null | grep -q "200" && echo "OK" || echo "NO_GITLAB"
```

**FIX:**
```bash
sudo apt install -y curl openssh-server ca-certificates tzdata perl
curl -sS https://packages.gitlab.com/install/repositories/gitlab/gitlab-ce/script.deb.sh | sudo bash
sudo EXTERNAL_URL="http://server:8081" GITLAB_ROOT_PASSWORD="{ADMIN_PASSWORD}" apt install -y gitlab-ce
```

Esperar a que GitLab arranque (puede tardar 2-5 minutos):
```bash
echo "Esperando a GitLab..."
for i in $(seq 1 60); do
  HTTP=$(curl -s -o /dev/null -w "%{http_code}" http://server:8081/-/health 2>/dev/null)
  if [ "$HTTP" = "200" ]; then echo "GitLab OK"; break; fi
  echo "Intento $i/60... HTTP $HTTP"
  sleep 10
done
```

Crear proyecto "project" en GitLab via API:
```bash
# Obtener token personal (crear via API con password del root)
GITLAB_TOKEN=$(curl -s -X POST "http://server:8081/oauth/token" \
  -d "grant_type=password&username=root&password={ADMIN_PASSWORD}" | jq -r '.access_token')

# Crear proyecto
curl -s -X POST "http://server:8081/api/v4/projects" \
  -H "Authorization: Bearer $GITLAB_TOKEN" \
  -d "name=project&visibility=private&initialize_with_readme=false"
```

Subir el repositorio a GitLab:
```bash
cd {RUTA_DEL_REPOSITORIO}
git remote remove gitlab 2>/dev/null || true
git remote add gitlab http://root:{ADMIN_PASSWORD}@server:8081/root/project.git
git push -u gitlab main
```

---

### PASO 7: Jenkins — Puerto 8080

**CHECK:**
```bash
curl -s -o /dev/null -w "%{http_code}" http://server:8080/login 2>/dev/null | grep -q "200" && echo "OK" || echo "NO_JENKINS"
```

**FIX:**
```bash
sudo apt install -y fontconfig openjdk-17-jre
sudo wget -O /usr/share/keyrings/jenkins-keyring.asc https://pkg.jenkins.io/debian-stable/jenkins.io-2023.key
echo "deb [signed-by=/usr/share/keyrings/jenkins-keyring.asc] https://pkg.jenkins.io/debian-stable binary/" | sudo tee /etc/apt/sources.list.d/jenkins.list > /dev/null
sudo apt update
sudo apt install -y jenkins
sudo usermod -aG docker jenkins
```

Configurar kubeconfig para Jenkins:
```bash
sudo mkdir -p /var/lib/jenkins/.kube
sudo cp /etc/rancher/k3s/k3s.yaml /var/lib/jenkins/.kube/config
sudo chown -R jenkins:jenkins /var/lib/jenkins/.kube
sudo chmod 600 /var/lib/jenkins/.kube/config
```

Instalar herramientas de build:
```bash
# .NET 10 SDK
wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
sudo /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

# Trivy
wget -qO - https://aquasecurity.github.io/trivy-repo/deb/public.key | gpg --dearmor | sudo tee /usr/share/keyrings/trivy.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/trivy.gpg] https://aquasecurity.github.io/trivy-repo/deb generic main" | sudo tee /etc/apt/sources.list.d/trivy.list
sudo apt update && sudo apt install -y trivy

# SonarScanner
sudo -u jenkins bash -c 'export DOTNET_ROOT=/usr/share/dotnet && export PATH=$PATH:/usr/share/dotnet && dotnet tool install --global dotnet-sonarscanner'
```

Reiniciar Jenkins:
```bash
sudo systemctl restart jenkins
```

**Configuración automática de Jenkins via API:**

Primero, obtener la contraseña inicial y el crumb:
```bash
JENKINS_PASS=$(sudo cat /var/lib/jenkins/secrets/initialAdminPassword)
echo "Contraseña inicial de Jenkins: $JENKINS_PASS"
```

Desactivar el wizard y crear admin (Jenkins necesita interacción manual para la configuración
inicial la primera vez — informa al humano de la contraseña y pídele que complete el setup wizard
en http://server:8080. Que instale los plugins sugeridos + plugin "GitLab"). Una vez hecho:

```bash
# URL base
JENKINS_URL="http://server:8080"
JENKINS_USER="admin"
JENKINS_PASS="{ADMIN_PASSWORD}"

# Obtener crumb CSRF
CRUMB=$(curl -s -u "$JENKINS_USER:$JENKINS_PASS" "$JENKINS_URL/crumbIssuer/api/json" | jq -r '.crumb')
CRUMB_HEADER=$(curl -s -u "$JENKINS_USER:$JENKINS_PASS" "$JENKINS_URL/crumbIssuer/api/json" | jq -r '.crumbRequestField')

# Crear credencial harbor-creds (Username/Password)
curl -s -u "$JENKINS_USER:$JENKINS_PASS" \
  -H "$CRUMB_HEADER: $CRUMB" \
  -X POST "$JENKINS_URL/credentials/store/system/domain/_/createCredentials" \
  --data-urlencode 'json={
    "": "0",
    "credentials": {
      "scope": "GLOBAL",
      "id": "harbor-creds",
      "username": "admin",
      "password": "'"$JENKINS_PASS"'",
      "description": "Harbor Registry",
      "$class": "com.cloudbees.plugins.credentials.impl.UsernamePasswordCredentialsImpl"
    }
  }'

# Crear credencial gitlab-creds (Username/Password)
curl -s -u "$JENKINS_USER:$JENKINS_PASS" \
  -H "$CRUMB_HEADER: $CRUMB" \
  -X POST "$JENKINS_URL/credentials/store/system/domain/_/createCredentials" \
  --data-urlencode 'json={
    "": "0",
    "credentials": {
      "scope": "GLOBAL",
      "id": "gitlab-creds",
      "username": "root",
      "password": "'"$JENKINS_PASS"'",
      "description": "GitLab Repository",
      "$class": "com.cloudbees.plugins.credentials.impl.UsernamePasswordCredentialsImpl"
    }
  }'
```

El token de SonarQube se creará en el PASO 8 y se añadirá después.

**Crear el pipeline job:**
```bash
# Crear job XML
cat > /tmp/jenkins-job.xml <<'JOBEOF'
<?xml version="1.0" encoding="UTF-8"?>
<flow-definition plugin="workflow-job">
  <description>Project CI/CD Pipeline</description>
  <keepDependencies>false</keepDependencies>
  <properties>
    <org.jenkinsci.plugins.gitlab.GitLabConnectionProperty plugin="gitlab-plugin">
      <gitLabConnection></gitLabConnection>
    </org.jenkinsci.plugins.gitlab.GitLabConnectionProperty>
    <com.dabsquared.gitlabjenkins.connection.GitLabConnectionProperty plugin="gitlab-plugin">
      <gitLabConnection></gitLabConnection>
    </com.dabsquared.gitlabjenkins.connection.GitLabConnectionProperty>
  </properties>
  <definition class="org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition" plugin="workflow-cps">
    <scm class="hudson.plugins.git.GitSCM" plugin="git">
      <configVersion>2</configVersion>
      <userRemoteConfigs>
        <hudson.plugins.git.UserRemoteConfig>
          <url>GITLAB_REPO_URL_PLACEHOLDER</url>
          <credentialsId>gitlab-creds</credentialsId>
        </hudson.plugins.git.UserRemoteConfig>
      </userRemoteConfigs>
      <branches>
        <hudson.plugins.git.BranchSpec>
          <name>*/main</name>
        </hudson.plugins.git.BranchSpec>
      </branches>
    </scm>
    <scriptPath>Jenkinsfile</scriptPath>
    <lightweight>true</lightweight>
  </definition>
  <triggers/>
  <disabled>false</disabled>
</flow-definition>
JOBEOF

# Reemplazar URL del repo
sed -i "s|GITLAB_REPO_URL_PLACEHOLDER|http://server:8081/root/project.git|g" /tmp/jenkins-job.xml

# Crear el job
curl -s -u "$JENKINS_USER:$JENKINS_PASS" \
  -H "$CRUMB_HEADER: $CRUMB" \
  -H "Content-Type: application/xml" \
  -X POST "$JENKINS_URL/createItem?name=project-pipeline" \
  --data-binary @/tmp/jenkins-job.xml
```

---

### PASO 8: SonarQube — Puerto 9000

**CHECK:**
```bash
curl -s -o /dev/null -w "%{http_code}" http://server:9000/api/system/status 2>/dev/null | grep -q "200" && echo "OK" || echo "NO_SONAR"
```

**FIX:**
```bash
sudo sysctl -w vm.max_map_count=524288
grep -q "vm.max_map_count" /etc/sysctl.conf || echo "vm.max_map_count=524288" | sudo tee -a /etc/sysctl.conf

sudo mkdir -p /opt/sonarqube && sudo chown $USER:$USER /opt/sonarqube

cat > /opt/sonarqube/docker-compose.yml <<EOF
services:
  sonarqube:
    image: sonarqube:lts-community
    container_name: sonarqube
    ports:
      - "9000:9000"
    environment:
      SONAR_JDBC_URL: jdbc:postgresql://sonar-db:5432/sonar
      SONAR_JDBC_USERNAME: sonar
      SONAR_JDBC_PASSWORD: sonar_password
    volumes:
      - sonarqube_data:/opt/sonarqube/data
      - sonarqube_extensions:/opt/sonarqube/extensions
      - sonarqube_logs:/opt/sonarqube/logs
    depends_on:
      - sonar-db
    restart: unless-stopped
  sonar-db:
    image: postgres:16-alpine
    container_name: sonar-db
    environment:
      POSTGRES_USER: sonar
      POSTGRES_PASSWORD: sonar_password
      POSTGRES_DB: sonar
    volumes:
      - sonar_postgres:/var/lib/postgresql/data
    restart: unless-stopped
volumes:
  sonarqube_data:
  sonarqube_extensions:
  sonarqube_logs:
  sonar_postgres:
EOF

cd /opt/sonarqube && docker compose up -d
```

Esperar a SonarQube (tarda 1-2 minutos):
```bash
echo "Esperando a SonarQube..."
for i in $(seq 1 30); do
  STATUS=$(curl -s http://server:9000/api/system/status 2>/dev/null | jq -r '.status' 2>/dev/null)
  if [ "$STATUS" = "UP" ]; then echo "SonarQube OK"; break; fi
  echo "Intento $i/30... status=$STATUS"
  sleep 10
done
```

Cambiar contraseña por defecto y crear token:
```bash
# Cambiar password de admin (default: admin/admin)
curl -s -u admin:admin -X POST "http://server:9000/api/users/change_password" \
  -d "login=admin&previousPassword=admin&password={ADMIN_PASSWORD}" || true

# Crear proyecto
curl -s -u "admin:{ADMIN_PASSWORD}" -X POST "http://server:9000/api/projects/create" \
  -d "name=project&project=project" || true

# Generar token para Jenkins
SONAR_TOKEN=$(curl -s -u "admin:{ADMIN_PASSWORD}" -X POST "http://server:9000/api/user_tokens/generate" \
  -d "name=jenkins" | jq -r '.token')
echo "SonarQube token: $SONAR_TOKEN"
```

Añadir token de SonarQube a Jenkins:
```bash
JENKINS_URL="http://server:8080"
JENKINS_USER="admin"
JENKINS_PASS="{ADMIN_PASSWORD}"
CRUMB=$(curl -s -u "$JENKINS_USER:$JENKINS_PASS" "$JENKINS_URL/crumbIssuer/api/json" | jq -r '.crumb')
CRUMB_HEADER=$(curl -s -u "$JENKINS_USER:$JENKINS_PASS" "$JENKINS_URL/crumbIssuer/api/json" | jq -r '.crumbRequestField')

curl -s -u "$JENKINS_USER:$JENKINS_PASS" \
  -H "$CRUMB_HEADER: $CRUMB" \
  -X POST "$JENKINS_URL/credentials/store/system/domain/_/createCredentials" \
  --data-urlencode 'json={
    "": "0",
    "credentials": {
      "scope": "GLOBAL",
      "id": "sonar-token",
      "secret": "'"$SONAR_TOKEN"'",
      "description": "SonarQube Analysis Token",
      "$class": "org.jenkinsci.plugins.plaincredentials.impl.StringCredentialsImpl"
    }
  }'
```

---

### PASO 9: MongoDB — Puerto 27017

**CHECK:**
```bash
docker ps --filter name=mongodb --format '{{.Status}}' | grep -q "Up" && echo "OK" || echo "NO_MONGODB"
```

**FIX:**
```bash
sudo mkdir -p /opt/mongodb && sudo chown $USER:$USER /opt/mongodb

cat > /opt/mongodb/docker-compose.yml <<EOF
services:
  mongodb:
    image: mongo:7
    container_name: mongodb
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: {ADMIN_PASSWORD}
    volumes:
      - mongodb_data:/data/db
      - mongodb_config:/data/configdb
    restart: unless-stopped
    command: ["mongod", "--bind_ip_all"]
volumes:
  mongodb_data:
  mongodb_config:
EOF

cd /opt/mongodb && docker compose up -d
```

---

### PASO 10: RabbitMQ — Puertos 5672 / 15672

**CHECK:**
```bash
docker ps --filter name=rabbitmq --format '{{.Status}}' | grep -q "Up" && echo "OK" || echo "NO_RABBITMQ"
curl -s -u "admin:{ADMIN_PASSWORD}" http://server:15672/api/overview > /dev/null 2>&1 && echo "OK" || echo "NO_RABBITMQ_API"
```

**FIX:**
```bash
sudo mkdir -p /opt/rabbitmq && sudo chown $USER:$USER /opt/rabbitmq

cat > /opt/rabbitmq/docker-compose.yml <<EOF
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: rabbitmq
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: {ADMIN_PASSWORD}
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    restart: unless-stopped
volumes:
  rabbitmq_data:
EOF

cd /opt/rabbitmq && docker compose up -d
```

---

### PASO 11: Redis — Puerto 6379

**CHECK:**
```bash
docker ps --filter name=redis --format '{{.Status}}' | grep -q "Up" && echo "OK" || echo "NO_REDIS"
docker exec redis redis-cli ping 2>/dev/null | grep -q "PONG" && echo "OK" || echo "NO_REDIS_PING"
```

**FIX:**
```bash
sudo mkdir -p /opt/redis && sudo chown $USER:$USER /opt/redis

cat > /opt/redis/docker-compose.yml <<EOF
services:
  redis:
    image: redis:7-alpine
    container_name: redis
    ports:
      - "6379:6379"
    command: redis-server --appendonly yes --maxmemory 256mb --maxmemory-policy allkeys-lru
    volumes:
      - redis_data:/data
    restart: unless-stopped
volumes:
  redis_data:
EOF

cd /opt/redis && docker compose up -d
```

---

### PASO 12: K8s — Namespace, Secrets y Observabilidad

**CHECK:**
```bash
kubectl get ns project > /dev/null 2>&1 && echo "OK" || echo "NO_NAMESPACE"
kubectl get secret project-secrets -n project > /dev/null 2>&1 && echo "OK" || echo "NO_SECRETS"
kubectl get deployment grafana -n project > /dev/null 2>&1 && echo "OK" || echo "NO_OBSERVABILITY"
```

**FIX:**

Primero, actualizar `k8s/secrets.yaml` con los valores reales del servidor. Usa la herramienta Edit
para reemplazar las connection strings:

```
mongodb-connection: "mongodb://admin:{ADMIN_PASSWORD}@{SERVER_IP}:27017"
rabbitmq-connection: "amqp://admin:{ADMIN_PASSWORD}@{SERVER_IP}:5672"
redis-connection: "{SERVER_IP}:6379"
jwt-secret: "{ADMIN_PASSWORD}_JWT_SECRET_32_CHARS!!"
sentry-dsn: ""
```

También actualizar `k8s/observability/grafana.yaml`: reemplazar `GF_SECURITY_ADMIN_PASSWORD` value
de "CAMBIAR" a "{ADMIN_PASSWORD}".

Luego aplicar:
```bash
cd {RUTA_DEL_REPOSITORIO}
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/secrets.yaml
kubectl apply -R -f k8s/observability/

# Crear secret para pull de imágenes de Harbor
kubectl create secret docker-registry harbor-registry \
  --docker-server=server:8085 \
  --docker-username=admin \
  --docker-password="{ADMIN_PASSWORD}" \
  -n project 2>/dev/null || true
```

Verificar que la observabilidad arranca:
```bash
echo "Esperando observabilidad..."
sleep 30
kubectl get pods -n project
```

---

### PASO 13: Build, Push y Deploy de la aplicación

**CHECK:**
```bash
kubectl get deployment api -n project > /dev/null 2>&1 && echo "OK" || echo "NO_APP"
curl -s -o /dev/null -w "%{http_code}" http://server:30080/health 2>/dev/null | grep -q "200" && echo "OK" || echo "NO_API_HEALTH"
```

**FIX:**
```bash
cd {RUTA_DEL_REPOSITORIO}

# Build de imágenes
docker build -t server:8085/project/api:v1 -f src/Project.Api/Dockerfile .
docker build -t server:8085/project/worker:v1 -f src/Project.Worker/Dockerfile .
docker build -t server:8085/project/frontend:v1 -f frontend/Dockerfile frontend/

# Tag como latest
docker tag server:8085/project/api:v1 server:8085/project/api:latest
docker tag server:8085/project/worker:v1 server:8085/project/worker:latest
docker tag server:8085/project/frontend:v1 server:8085/project/frontend:latest

# Login y push a Harbor
echo "{ADMIN_PASSWORD}" | docker login server:8085 -u admin --password-stdin
docker push server:8085/project/api:v1
docker push server:8085/project/api:latest
docker push server:8085/project/worker:v1
docker push server:8085/project/worker:latest
docker push server:8085/project/frontend:v1
docker push server:8085/project/frontend:latest

# Desplegar
kubectl apply -f k8s/redis-deployment.yaml
kubectl apply -f k8s/api-deployment.yaml
kubectl apply -f k8s/worker-deployment.yaml
kubectl apply -f k8s/frontend-deployment.yaml
kubectl apply -f k8s/ingress.yaml

# Esperar rollout
kubectl rollout status deployment/api -n project --timeout=120s
kubectl rollout status deployment/worker -n project --timeout=120s
kubectl rollout status deployment/frontend -n project --timeout=60s
```

---

### PASO 14: Configurar webhook GitLab → Jenkins

**CHECK:**
```bash
# Verificar que el webhook existe
GITLAB_TOKEN=$(curl -s -X POST "http://server:8081/oauth/token" \
  -d "grant_type=password&username=root&password={ADMIN_PASSWORD}" | jq -r '.access_token')
PROJECT_ID=$(curl -s -H "Authorization: Bearer $GITLAB_TOKEN" "http://server:8081/api/v4/projects" | jq -r '.[0].id')
HOOKS=$(curl -s -H "Authorization: Bearer $GITLAB_TOKEN" "http://server:8081/api/v4/projects/$PROJECT_ID/hooks" | jq length)
[ "$HOOKS" -gt "0" ] && echo "OK" || echo "NO_WEBHOOK"
```

**FIX:**
```bash
GITLAB_TOKEN=$(curl -s -X POST "http://server:8081/oauth/token" \
  -d "grant_type=password&username=root&password={ADMIN_PASSWORD}" | jq -r '.access_token')
PROJECT_ID=$(curl -s -H "Authorization: Bearer $GITLAB_TOKEN" "http://server:8081/api/v4/projects" | jq -r '.[0].id')

curl -s -X POST "http://server:8081/api/v4/projects/$PROJECT_ID/hooks" \
  -H "Authorization: Bearer $GITLAB_TOKEN" \
  -d "url=http://server:8080/project/project-pipeline&push_events=true&enable_ssl_verification=false"
```

---

### PASO 15: Verificación final

Ejecuta TODAS estas comprobaciones. Todas deben dar OK:

```bash
echo "=== VERIFICACION COMPLETA ==="

echo -n "Docker:      "; docker version > /dev/null 2>&1 && echo "OK" || echo "FALLO"
echo -n "K3s:         "; kubectl get nodes 2>/dev/null | grep -q "Ready" && echo "OK" || echo "FALLO"
echo -n "Harbor:      "; curl -sf http://server:8085/api/v2.0/health > /dev/null && echo "OK" || echo "FALLO"
echo -n "GitLab:      "; curl -sf http://server:8081/-/health > /dev/null && echo "OK" || echo "FALLO"
echo -n "Jenkins:     "; curl -sf http://server:8080/login > /dev/null && echo "OK" || echo "FALLO"
echo -n "SonarQube:   "; curl -sf http://server:9000/api/system/status > /dev/null && echo "OK" || echo "FALLO"
echo -n "MongoDB:     "; docker exec mongodb mongosh --eval "db.runCommand({ping:1})" -u admin -p "{ADMIN_PASSWORD}" --authenticationDatabase admin --quiet > /dev/null 2>&1 && echo "OK" || echo "FALLO"
echo -n "RabbitMQ:    "; curl -sf -u "admin:{ADMIN_PASSWORD}" http://server:15672/api/overview > /dev/null && echo "OK" || echo "FALLO"
echo -n "Redis:       "; docker exec redis redis-cli ping 2>/dev/null | grep -q PONG && echo "OK" || echo "FALLO"
echo -n "API:         "; curl -sf http://server:30080/health > /dev/null && echo "OK" || echo "FALLO"
echo -n "Frontend:    "; curl -sf http://server:30081/ > /dev/null && echo "OK" || echo "FALLO"
echo -n "Grafana:     "; curl -sf http://server:30030/api/health > /dev/null && echo "OK" || echo "FALLO"

echo ""
echo "=== PODS K8S ==="
kubectl get pods -n project

echo ""
echo "=== URLS ==="
echo "API:          http://server:30080"
echo "API Docs:     http://server:30080/scalar/v1"
echo "Frontend:     http://server:30081"
echo "Jenkins:      http://server:8080"
echo "GitLab:       http://server:8081"
echo "Harbor:       http://server:8085"
echo "SonarQube:    http://server:9000"
echo "RabbitMQ:     http://server:15672"
echo "Grafana:      http://server:30030"
```

Si algún paso da FALLO, vuelve a la sección correspondiente del playbook y ejecuta el FIX.

---

## COMANDOS DE MANTENIMIENTO

Cuando el humano te pida verificar, reparar o mantener la infraestructura, usa estos comandos:

### Ver estado general
```bash
kubectl get pods -n project
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

### Logs de la aplicación
```bash
kubectl logs -f deployment/api -n project --tail=50
kubectl logs -f deployment/worker -n project --tail=50
```

### Reiniciar un servicio
```bash
# Aplicación en K8s
kubectl rollout restart deployment/api -n project
kubectl rollout restart deployment/worker -n project

# Infraestructura Docker
cd /opt/mongodb && docker compose restart
cd /opt/rabbitmq && docker compose restart
cd /opt/redis && docker compose restart
```

### Backup de MongoDB
```bash
docker exec mongodb mongodump -u admin -p "{ADMIN_PASSWORD}" --authenticationDatabase admin --archive=/tmp/backup.gz --gzip
docker cp mongodb:/tmp/backup.gz /opt/backups/backup-$(date +%Y%m%d).gz
```

### Redesplegar tras cambios de código
```bash
cd {RUTA_DEL_REPOSITORIO}
git push gitlab main
# El webhook dispara Jenkins automáticamente
# Alternativamente, lanza manual: curl -X POST http://admin:{ADMIN_PASSWORD}@server:8080/job/project-pipeline/build
```

---

## REGLAS PARA CLAUDE

1. **Nunca modifiques reglas de negocio** sin que el humano te lo pida explícitamente.
   Las reglas de negocio están en: Domain/Entities, Domain/ValueObjects, Application/Commands,
   Application/Queries, Application/Validators.

2. **La infraestructura es tu responsabilidad.** Si algo falla (build, deploy, servicios caídos),
   diagnostica y repara sin preguntar. Solo pregunta si necesitas la contraseña o la IP.

3. **Sigue el playbook secuencialmente.** Los pasos tienen dependencias. No saltes pasos.

4. **Cada paso es idempotente.** Siempre ejecuta CHECK antes de FIX. Si CHECK pasa, salta al siguiente.

5. **Reemplaza placeholders** al ejecutar:
   - `{SERVER_IP}` → IP real del servidor
   - `{ADMIN_PASSWORD}` → Contraseña maestra que te dio el humano
   - `{RUTA_DEL_REPOSITORIO}` → Directorio donde está clonado este repo

6. **Si el humano dice "despliega"**, ejecuta el playbook completo (pasos 0-15).
   **Si dice "verifica"**, ejecuta solo los CHECK de cada paso y reporta estado.
   **Si dice "repara X"**, ve directamente al paso correspondiente.

7. **Jenkins necesita setup wizard manual la primera vez.** Informa al humano y espera.
   Todo lo demás es automatizable.

8. **El código se buildea y despliega siempre desde el Jenkinsfile.** No hagas builds manuales
   salvo en el PASO 13 (despliegue inicial antes de que Jenkins esté listo).

9. **Sé proactivo con los errores.** Si un pod está en CrashLoopBackOff, lee los logs,
   identifica la causa (connection string, recurso no disponible, etc.) y arréglalo.

10. **Mapa de puertos** (memorízalo):
    - 8080: Jenkins
    - 8081: GitLab
    - 8085: Harbor
    - 9000: SonarQube
    - 27017: MongoDB
    - 5672/15672: RabbitMQ
    - 6379: Redis
    - 30080: API (K8s)
    - 30081: Frontend (K8s)
    - 30030: Grafana (K8s)
