# Despliegue de Infraestructura en Servidor Ubuntu (Home Lab)

Guía paso a paso para desplegar toda la infraestructura necesaria en un servidor Ubuntu doméstico,
convirtiendo esta solución en una plataforma de prácticas de microservicios con CI/CD completo.

---

## Tabla de Contenidos

1. [Requisitos de Hardware y Red](#1-requisitos-de-hardware-y-red)
2. [Preparación del Servidor Ubuntu](#2-preparación-del-servidor-ubuntu)
3. [Docker y Docker Compose](#3-docker-y-docker-compose)
4. [K3s — Kubernetes Ligero](#4-k3s--kubernetes-ligero)
5. [Harbor — Registro de Contenedores](#5-harbor--registro-de-contenedores)
6. [Jenkins — CI/CD](#6-jenkins--cicd)
7. [SonarQube — Análisis de Código](#7-sonarqube--análisis-de-código)
8. [MongoDB — Base de Datos](#8-mongodb--base-de-datos)
9. [RabbitMQ — Mensajería](#9-rabbitmq--mensajería)
10. [Redis — Caché Distribuida](#10-redis--caché-distribuida)
11. [Stack de Observabilidad](#11-stack-de-observabilidad)
12. [Despliegue de la Aplicación](#12-despliegue-de-la-aplicación)
13. [DNS y Acceso en Red Local](#13-dns-y-acceso-en-red-local)
14. [Mapa de Puertos](#14-mapa-de-puertos)
15. [Mantenimiento](#15-mantenimiento)
16. [Solución de Problemas](#16-solución-de-problemas)

---

## 1. Requisitos de Hardware y Red

### Hardware Mínimo

| Recurso | Mínimo | Recomendado |
|---------|--------|-------------|
| CPU | 4 cores | 8 cores |
| RAM | 16 GB | 32 GB |
| Disco | 100 GB SSD | 256 GB NVMe |
| Red | 1 Gbps | 1 Gbps |

### Distribución Estimada de Memoria

| Componente | RAM |
|------------|-----|
| Sistema operativo | ~1 GB |
| Docker daemon | ~0.5 GB |
| K3s (control plane) | ~0.5 GB |
| Harbor | ~2 GB |
| Jenkins | ~1 GB |
| SonarQube + PostgreSQL | ~3 GB |
| MongoDB | ~1 GB |
| RabbitMQ | ~0.5 GB |
| Redis | ~0.25 GB |
| Observabilidad (Prometheus, Grafana, Loki, Tempo) | ~2 GB |
| Aplicación (API×2 + Worker + Frontend×4) | ~1.5 GB |
| **Total** | **~13.75 GB** |

### Red

- El servidor debe tener una IP fija en la red local (configurar en el router o con netplan).
- Si quieres acceder desde tu PC de desarrollo, asegúrate de que ambos equipos estén en la misma subred.
- No es necesario abrir puertos al exterior para un entorno de prácticas.

---

## 2. Preparación del Servidor Ubuntu

### 2.1. Instalación de Ubuntu Server

Descarga [Ubuntu Server 24.04 LTS](https://ubuntu.com/download/server) e instálalo con las opciones por defecto.
Durante la instalación, marca la opción de instalar OpenSSH Server para poder conectarte remotamente.

### 2.2. Configuración Inicial

Conéctate por SSH desde tu PC de desarrollo:

```bash
ssh tu-usuario@IP-DEL-SERVIDOR
```

Actualiza el sistema:

```bash
sudo apt update && sudo apt upgrade -y
```

Instala paquetes esenciales:

```bash
sudo apt install -y \
  curl wget git unzip apt-transport-https \
  ca-certificates gnupg lsb-release \
  net-tools htop
```

### 2.3. IP Fija con Netplan

Edita la configuración de red para asignar una IP fija:

```bash
sudo nano /etc/netplan/01-netcfg.yaml
```

```yaml
network:
  version: 2
  ethernets:
    enp0s3:                        # Ajusta al nombre de tu interfaz (ip link show)
      dhcp4: no
      addresses:
        - 192.168.1.100/24         # IP fija que elijas
      routes:
        - to: default
          via: 192.168.1.1         # Tu gateway (normalmente el router)
      nameservers:
        addresses: [8.8.8.8, 1.1.1.1]
```

```bash
sudo netplan apply
```

### 2.4. Nombre del Servidor

Define un hostname descriptivo (este será el `server` que referencia el proyecto):

```bash
sudo hostnamectl set-hostname server
```

Añade la resolución local:

```bash
echo "192.168.1.100 server" | sudo tee -a /etc/hosts
```

> **Importante:** En toda esta guía, `server` y `192.168.1.100` son la IP/hostname de tu servidor Ubuntu.
> Ajústalos a tu red. El proyecto usa `server` como hostname en Jenkinsfile, Dockerfiles e imágenes K8s.

---

## 3. Docker y Docker Compose

### 3.1. Instalar Docker Engine

```bash
# Añadir repositorio oficial de Docker
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

### 3.2. Configurar Docker sin sudo

```bash
sudo usermod -aG docker $USER
newgrp docker
```

### 3.3. Configurar Docker para Harbor (registro inseguro)

Harbor usará HTTP en la red local. Docker necesita confiar en él:

```bash
sudo mkdir -p /etc/docker
sudo tee /etc/docker/daemon.json <<EOF
{
  "insecure-registries": ["server:8085"],
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  }
}
EOF

sudo systemctl restart docker
```

### 3.4. Verificar

```bash
docker version
docker compose version
```

---

## 4. K3s — Kubernetes Ligero

K3s es la distribución de Kubernetes ideal para un servidor doméstico: consume pocos recursos y viene con todo incluido.

### 4.1. Instalar K3s

```bash
curl -sfL https://get.k3s.io | sh -s - \
  --write-kubeconfig-mode 644 \
  --disable traefik \
  --docker
```

> - `--disable traefik`: Deshabilitamos Traefik porque usaremos Nginx Ingress Controller.
> - `--docker`: Usa Docker como runtime en lugar de containerd (permite compartir imágenes con Jenkins).

### 4.2. Verificar Instalación

```bash
sudo kubectl get nodes
# Deberías ver: server   Ready   control-plane,master   ...

sudo kubectl get pods -A
# Verás los componentes del sistema de K3s
```

### 4.3. Configurar kubectl para tu Usuario

```bash
mkdir -p ~/.kube
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $USER:$USER ~/.kube/config
chmod 600 ~/.kube/config
```

Verifica:

```bash
kubectl get nodes
```

### 4.4. Configurar K3s para Harbor (registro inseguro)

```bash
sudo mkdir -p /etc/rancher/k3s
sudo tee /etc/rancher/k3s/registries.yaml <<EOF
mirrors:
  "server:8085":
    endpoint:
      - "http://server:8085"
EOF

sudo systemctl restart k3s
```

### 4.5. Instalar Nginx Ingress Controller

```bash
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/cloud/deploy.yaml
```

Verifica que esté corriendo:

```bash
kubectl get pods -n ingress-nginx
# nginx-ingress-controller-xxxxx   Running
```

### 4.6. Instalar Cert-Manager (Opcional — para TLS con Let's Encrypt)

Para un home lab, los certificados TLS reales no son necesarios, pero si quieres practicar:

```bash
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.14.5/cert-manager.yaml
```

---

## 5. Harbor — Registro de Contenedores

Harbor es el registro privado donde Jenkins pushea las imágenes Docker que luego K3s descarga para desplegar.

### 5.1. Descargar Harbor

```bash
cd /opt
sudo mkdir harbor && sudo chown $USER:$USER harbor
cd harbor

HARBOR_VERSION="v2.11.0"
wget https://github.com/goharbor/harbor/releases/download/${HARBOR_VERSION}/harbor-offline-installer-${HARBOR_VERSION}.tgz
tar xzf harbor-offline-installer-${HARBOR_VERSION}.tgz
cd harbor
```

### 5.2. Configurar Harbor

```bash
cp harbor.yml.tmpl harbor.yml
```

Edita `harbor.yml`:

```bash
nano harbor.yml
```

Cambia estos valores:

```yaml
hostname: server

# Puerto HTTP (comentar la sección https para home lab)
http:
  port: 8085

# Comentar toda la sección https:
# https:
#   port: 443
#   certificate: ...
#   private_key: ...

harbor_admin_password: TU_PASSWORD_ADMIN    # Cámbiala

# Base de datos interna
database:
  password: TU_PASSWORD_DB

# Directorio de datos
data_volume: /data/harbor
```

### 5.3. Instalar Harbor

```bash
sudo mkdir -p /data/harbor
sudo ./install.sh --with-trivy
```

> `--with-trivy` instala el escáner de vulnerabilidades integrado.

### 5.4. Verificar Harbor

Abre en el navegador:

```
http://server:8085
```

- **Usuario:** admin
- **Password:** la que configuraste en `harbor.yml`

### 5.5. Crear el Proyecto en Harbor

1. Inicia sesión en la web de Harbor.
2. Ve a **Projects** → **New Project**.
3. Nombre: `project` (coincide con `HARBOR_PROJECT` del Jenkinsfile).
4. Acceso: Public (para simplificar en home lab).

### 5.6. Verificar Login desde Docker

```bash
docker login server:8085
# Usuario: admin
# Password: tu password
```

### 5.7. Iniciar Harbor con el Sistema

Harbor usa Docker Compose internamente:

```bash
# Crear servicio systemd para Harbor
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

---

## 6. Jenkins — CI/CD

### 6.1. Instalar Jenkins

```bash
# Java 17 (requisito de Jenkins)
sudo apt install -y fontconfig openjdk-17-jre

# Repositorio de Jenkins
sudo wget -O /usr/share/keyrings/jenkins-keyring.asc \
  https://pkg.jenkins.io/debian-stable/jenkins.io-2023.key

echo "deb [signed-by=/usr/share/keyrings/jenkins-keyring.asc] \
  https://pkg.jenkins.io/debian-stable binary/" | \
  sudo tee /etc/apt/sources.list.d/jenkins.list > /dev/null

sudo apt update
sudo apt install -y jenkins
```

### 6.2. Configurar Jenkins

Jenkins necesita acceso a Docker y kubectl:

```bash
# Añadir jenkins al grupo docker
sudo usermod -aG docker jenkins

# Copiar kubeconfig para Jenkins
sudo mkdir -p /var/lib/jenkins/.kube
sudo cp /etc/rancher/k3s/k3s.yaml /var/lib/jenkins/.kube/config
sudo chown -R jenkins:jenkins /var/lib/jenkins/.kube
sudo chmod 600 /var/lib/jenkins/.kube/config
```

Configurar Docker insecure registry también para el usuario jenkins:

```bash
sudo systemctl restart jenkins
```

### 6.3. Instalar Herramientas en Jenkins

Jenkins necesita .NET SDK, Trivy y SonarScanner:

```bash
# .NET 10 SDK
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

# Trivy (escáner de seguridad)
sudo apt install -y wget apt-transport-https gnupg
wget -qO - https://aquasecurity.github.io/trivy-repo/deb/public.key | \
  gpg --dearmor | sudo tee /usr/share/keyrings/trivy.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/trivy.gpg] https://aquasecurity.github.io/trivy-repo/deb generic main" | \
  sudo tee /etc/apt/sources.list.d/trivy.list
sudo apt update
sudo apt install -y trivy

# SonarScanner para .NET (como usuario jenkins)
sudo -u jenkins bash -c 'dotnet tool install --global dotnet-sonarscanner'
```

### 6.4. Acceder a Jenkins

Abre en el navegador:

```
http://server:8080
```

Obtén la contraseña inicial:

```bash
sudo cat /var/lib/jenkins/secrets/initialAdminPassword
```

### 6.5. Configurar Credenciales en Jenkins

Una vez dentro de Jenkins, ve a **Manage Jenkins → Credentials → System → Global credentials** y añade:

| ID | Tipo | Descripción |
|----|------|-------------|
| `harbor-creds` | Username/Password | Usuario y password de Harbor |
| `sonar-token` | Secret text | Token de SonarQube (se crea en el paso 7) |

### 6.6. Crear el Pipeline

1. **New Item** → nombre: `project` → tipo: **Pipeline**.
2. En Pipeline → Definition: **Pipeline script from SCM**.
3. SCM: **Git** → URL del repositorio.
4. Script Path: `Jenkinsfile`.
5. Branch: `*/main`.

---

## 7. SonarQube — Análisis de Código

### 7.1. Desplegar SonarQube con Docker Compose

```bash
sudo mkdir -p /opt/sonarqube
sudo chown $USER:$USER /opt/sonarqube
```

Crea `/opt/sonarqube/docker-compose.yml`:

```yaml
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
```

Requisito de sistema para SonarQube (Elasticsearch):

```bash
sudo sysctl -w vm.max_map_count=524288
echo "vm.max_map_count=524288" | sudo tee -a /etc/sysctl.conf
```

Arrancar:

```bash
cd /opt/sonarqube
docker compose up -d
```

### 7.2. Configurar SonarQube

Accede a `http://server:9000`:

- **Usuario:** admin
- **Password:** admin (te pedirá cambiarla)

Crea un token:

1. **My Account** → **Security** → **Generate Tokens**.
2. Nombre: `jenkins`, tipo: **Global Analysis Token**.
3. Copia el token y añádelo como credencial `sonar-token` en Jenkins (paso 6.5).

Crea el proyecto:

1. **Projects** → **Create Project** → **Manually**.
2. Project Key: `project` (coincide con `/k:"project"` del Jenkinsfile).

---

## 8. MongoDB — Base de Datos

MongoDB corre fuera de K3s como servicio de infraestructura compartido.

### 8.1. Desplegar MongoDB

```bash
sudo mkdir -p /opt/mongodb
sudo chown $USER:$USER /opt/mongodb
```

Crea `/opt/mongodb/docker-compose.yml`:

```yaml
services:
  mongodb:
    image: mongo:7
    container_name: mongodb
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: CAMBIAR_POR_PASSWORD_SEGURA
    volumes:
      - mongodb_data:/data/db
      - mongodb_config:/data/configdb
    restart: unless-stopped
    command: ["mongod", "--bind_ip_all"]

volumes:
  mongodb_data:
  mongodb_config:
```

```bash
cd /opt/mongodb
docker compose up -d
```

### 8.2. Verificar

```bash
docker exec -it mongodb mongosh -u admin -p CAMBIAR_POR_PASSWORD_SEGURA --authenticationDatabase admin
```

```javascript
// Dentro de mongosh
show dbs
exit
```

### 8.3. Crear Usuario de Aplicación (Opcional, Recomendado)

```bash
docker exec -it mongodb mongosh -u admin -p CAMBIAR_POR_PASSWORD_SEGURA --authenticationDatabase admin
```

```javascript
use project
db.createUser({
  user: "projectapp",
  pwd: "PASSWORD_APP",
  roles: [{ role: "readWrite", db: "project" }]
})

use project_dev
db.createUser({
  user: "projectapp",
  pwd: "PASSWORD_APP",
  roles: [{ role: "readWrite", db: "project_dev" }]
})

use project_staging
db.createUser({
  user: "projectapp",
  pwd: "PASSWORD_APP",
  roles: [{ role: "readWrite", db: "project_staging" }]
})
```

### 8.4. Hacer MongoDB Accesible desde K3s

K3s necesita llegar a MongoDB. Como corre en Docker fuera del cluster, usa la IP del host:

```
mongodb://admin:PASSWORD@192.168.1.100:27017
```

Actualiza `k8s/secrets.yaml` con esta connection string real.

---

## 9. RabbitMQ — Mensajería

### 9.1. Desplegar RabbitMQ

```bash
sudo mkdir -p /opt/rabbitmq
sudo chown $USER:$USER /opt/rabbitmq
```

Crea `/opt/rabbitmq/docker-compose.yml`:

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: rabbitmq
    ports:
      - "5672:5672"       # AMQP
      - "15672:15672"     # Management UI
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: CAMBIAR_POR_PASSWORD_SEGURA
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    restart: unless-stopped

volumes:
  rabbitmq_data:
```

```bash
cd /opt/rabbitmq
docker compose up -d
```

### 9.2. Verificar

Panel de administración: `http://server:15672`

- **Usuario:** admin
- **Password:** la que configuraste

### 9.3. Habilitar Quorum Queues

MassTransit configura las colas automáticamente como Quorum Queues al arrancar la aplicación.
No necesitas configuración adicional en RabbitMQ. Solo asegúrate de que el plugin de management
esté activo (ya incluido en la imagen `rabbitmq:3-management`).

### 9.4. Connection String para K8s

```
amqp://admin:PASSWORD@192.168.1.100:5672
```

---

## 10. Redis — Caché Distribuida

### 10.1. Desplegar Redis

```bash
sudo mkdir -p /opt/redis
sudo chown $USER:$USER /opt/redis
```

Crea `/opt/redis/docker-compose.yml`:

```yaml
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
```

```bash
cd /opt/redis
docker compose up -d
```

### 10.2. Verificar

```bash
docker exec -it redis redis-cli ping
# PONG
```

### 10.3. Connection String para K8s

```
192.168.1.100:6379
```

> **Nota:** En un home lab no es necesario habilitar autenticación en Redis. Si lo deseas:
> `command: redis-server --appendonly yes --requirepass TU_PASSWORD`
> Connection string: `192.168.1.100:6379,password=TU_PASSWORD`

---

## Nota Sobre la Arquitectura de Red

Los manifiestos K8s del proyecto (`k8s/secrets.yaml`, appsettings de Staging/Production) referencian
los servicios de infraestructura como `*.infrastructure.svc.cluster.local`. Esto asume un namespace
`infrastructure` dentro de K8s donde vivirían MongoDB, RabbitMQ y Redis como pods.

**En esta guía hemos elegido un enfoque diferente y más práctico para home lab:**

- MongoDB, RabbitMQ y Redis se despliegan como **contenedores Docker fuera de K3s**.
- Son servicios con estado (stateful) que se benefician de persistencia directa en disco.
- Se acceden desde K3s mediante la **IP del host** (`192.168.1.100`).
- Esto simplifica los backups, la gestión y evita la complejidad de PersistentVolumes en K3s.

Por eso, en el paso 12.1 se actualizan los secrets con IPs reales en lugar de nombres DNS internos de K8s.

Si en el futuro prefieres migrar a una arquitectura 100% Kubernetes, puedes:
1. Crear el namespace `infrastructure` → `kubectl create namespace infrastructure`.
2. Desplegar MongoDB, RabbitMQ y Redis como StatefulSets con PersistentVolumeClaims.
3. Restaurar las connection strings originales con `*.infrastructure.svc.cluster.local`.

---

## 11. Stack de Observabilidad

La observabilidad se despliega **dentro de K3s** usando los manifiestos del proyecto.
Los servicios de infraestructura (MongoDB, RabbitMQ, Redis) van fuera; la observabilidad va dentro.

### 11.1. Crear el Namespace

```bash
kubectl apply -f k8s/namespace.yaml
```

### 11.2. Desplegar Stack de Observabilidad

Aplica todos los manifiestos de observabilidad:

```bash
kubectl apply -f k8s/observability/loki.yaml
kubectl apply -f k8s/observability/tempo.yaml
kubectl apply -f k8s/observability/prometheus.yaml
kubectl apply -f k8s/observability/otel-collector.yaml
kubectl apply -f k8s/observability/fluentbit.yaml
kubectl apply -f k8s/observability/grafana.yaml
```

### 11.3. Verificar que Todo Esté Corriendo

```bash
kubectl get pods -n project
```

Deberías ver algo como:

```
NAME                              READY   STATUS    RESTARTS   AGE
loki-xxxxx                        1/1     Running   0          1m
tempo-xxxxx                       1/1     Running   0          1m
prometheus-xxxxx                  1/1     Running   0          1m
alertmanager-xxxxx                1/1     Running   0          1m
otel-collector-xxxxx              1/1     Running   0          1m
grafana-xxxxx                     1/1     Running   0          1m
fluentbit-xxxxx                   1/1     Running   0          1m
```

### 11.4. Acceder a Grafana

Grafana está expuesto como NodePort en el puerto 30030:

```
http://server:30030
```

- **Usuario:** admin
- **Password:** la configurada en `grafana.yaml` (por defecto `CAMBIAR` — modifícala antes de aplicar)

Los datasources de Prometheus, Loki y Tempo ya están pre-configurados.

### 11.5. Flujo de Datos de Observabilidad

```
┌─────────────────────────────────────────────────────────────┐
│                    Aplicación (.NET)                         │
│  Serilog ──────────────────────────► stdout (JSON)          │
│  OpenTelemetry ── traces/metrics ──► OTel Collector (:4317) │
└──────────────────────┬──────────────────────┬───────────────┘
                       │                      │
              ┌────────▼────────┐    ┌────────▼────────┐
              │   Fluent Bit    │    │  OTel Collector  │
              │  (DaemonSet)    │    │                  │
              └────────┬────────┘    └──┬─────┬────────┘
                       │               │     │
              ┌────────▼────────┐  ┌───▼──┐  │
              │      Loki       │  │Tempo │  │
              │   (logs)        │  │(trc) │  │
              └────────┬────────┘  └──┬───┘  │
                       │              │  ┌───▼──────┐
                       │              │  │Prometheus│
                       │              │  │ (metrics)│
                       │              │  └───┬──────┘
              ┌────────▼──────────────▼──────▼──────┐
              │            Grafana (:30030)          │
              │  Dashboards · Alertas · Exploración  │
              └─────────────────────────────────────┘
```

---

## 12. Despliegue de la Aplicación

### 12.1. Actualizar Secrets con Valores Reales

Edita `k8s/secrets.yaml` con las connection strings reales de tu servidor:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: project-secrets
  namespace: project
type: Opaque
stringData:
  mongodb-connection: "mongodb://admin:TU_PASSWORD@192.168.1.100:27017"
  rabbitmq-connection: "amqp://admin:TU_PASSWORD@192.168.1.100:5672"
  redis-connection: "192.168.1.100:6379"
  jwt-secret: "un-secreto-de-al-menos-32-caracteres-que-sea-seguro"
  sentry-dsn: ""
```

> **Sentry DSN:** Si quieres usar Sentry, crea una cuenta gratuita en [sentry.io](https://sentry.io),
> crea un proyecto .NET y copia el DSN. Si no, déjalo vacío (la aplicación funciona sin él).

### 12.2. Aplicar Manifiestos Base

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/secrets.yaml
```

### 12.3. Desplegar Redis dentro de K8s (Opcional)

El proyecto incluye un manifiesto para Redis dentro de K8s. Si prefieres usar el Redis externo
(paso 10), actualiza la connection string en secrets. Si prefieres un Redis dentro del cluster:

```bash
kubectl apply -f k8s/redis-deployment.yaml
```

En ese caso, la connection string sería: `redis.project.svc.cluster.local:6379`

### 12.4. Build y Push de Imágenes (Manual, sin Jenkins)

Si quieres desplegar antes de tener Jenkins configurado:

```bash
# Desde el directorio raíz del proyecto
cd /ruta/al/proyecto

# Build de imágenes
docker build -t server:8085/project/api:v1 -f src/Project.Api/Dockerfile .
docker build -t server:8085/project/worker:v1 -f src/Project.Worker/Dockerfile .
docker build -t server:8085/project/frontend:v1 -f frontend/Dockerfile frontend/

# Login en Harbor
docker login server:8085

# Push
docker push server:8085/project/api:v1
docker push server:8085/project/worker:v1
docker push server:8085/project/frontend:v1
```

Actualiza los tags en los manifiestos de despliegue de `latest` a `v1` (o tagea también como `latest`):

```bash
docker tag server:8085/project/api:v1 server:8085/project/api:latest
docker tag server:8085/project/worker:v1 server:8085/project/worker:latest
docker tag server:8085/project/frontend:v1 server:8085/project/frontend:latest

docker push server:8085/project/api:latest
docker push server:8085/project/worker:latest
docker push server:8085/project/frontend:latest
```

### 12.5. Desplegar la Aplicación

```bash
kubectl apply -f k8s/api-deployment.yaml
kubectl apply -f k8s/worker-deployment.yaml
kubectl apply -f k8s/frontend-deployment.yaml
kubectl apply -f k8s/ingress.yaml
```

### 12.6. Verificar

```bash
kubectl get pods -n project
```

```
NAME                        READY   STATUS    RESTARTS   AGE
api-xxxxx-yyy               1/1     Running   0          30s
api-xxxxx-zzz               1/1     Running   0          30s
worker-xxxxx-yyy             1/1     Running   0          30s
frontend-xxxxx-aaa           1/1     Running   0          30s
frontend-xxxxx-bbb           1/1     Running   0          30s
frontend-xxxxx-ccc           1/1     Running   0          30s
frontend-xxxxx-ddd           1/1     Running   0          30s
redis-xxxxx-yyy              1/1     Running   0          30s
...observability pods...
```

Comprueba la salud de la API:

```bash
curl http://server:30080/health
# Healthy
```

Comprueba el frontend:

```bash
curl -s -o /dev/null -w "%{http_code}" http://server:30081/
# 200
```

---

## 13. DNS y Acceso en Red Local

### 13.1. Archivo hosts en tu PC de Desarrollo

Para acceder a los servicios por nombre desde tu PC Windows/Mac/Linux, edita el archivo hosts:

**Windows** (`C:\Windows\System32\drivers\etc\hosts`):
```
192.168.1.100   server
```

**Linux/Mac** (`/etc/hosts`):
```
192.168.1.100   server
```

### 13.2. Si Usas Ingress con Dominio

El `ingress.yaml` define `project.example.com`. Para usarlo en red local:

```
192.168.1.100   project.example.com
```

Añade esta línea en tu archivo hosts y podrás acceder a:

- Frontend: `http://project.example.com`
- API: `http://project.example.com/api`

### 13.3. Acceso Directo por NodePort (Sin Ingress)

Para un home lab, los NodePorts son más sencillos:

| Servicio | URL |
|----------|-----|
| API | `http://server:30080` |
| Frontend | `http://server:30081` |
| Grafana | `http://server:30030` |

---

## 14. Mapa de Puertos

Referencia rápida de todos los puertos usados en la infraestructura:

| Puerto | Servicio | Protocolo | Ubicación |
|--------|----------|-----------|-----------|
| 8080 | Jenkins | HTTP | Sistema (apt) |
| 8081 | GitLab | HTTP | Docker Compose / sistema |
| 8085 | Harbor | HTTP | Docker Compose |
| 9000 | SonarQube | HTTP | Docker Compose |
| 27017 | MongoDB | TCP | Docker Compose |
| 5672 | RabbitMQ (AMQP) | TCP | Docker Compose |
| 15672 | RabbitMQ Management | HTTP | Docker Compose |
| 6379 | Redis | TCP | Docker Compose |
| 30080 | API (K8s NodePort) | HTTP | K3s |
| 30081 | Frontend (K8s NodePort) | HTTP | K3s |
| 30030 | Grafana (K8s NodePort) | HTTP | K3s |
| 6443 | K3s API Server | HTTPS | K3s |

> **Nota sobre GitLab:** Esta guía no cubre la instalación de GitLab porque puede alojarse
> en el mismo servidor, en otro equipo de la red, o usar gitlab.com. El pipeline asume GitLab
> en `http://server:8081`. Consulta la [documentación oficial de GitLab](https://about.gitlab.com/install/)
> para su instalación. Si usas otro puerto o host, ajusta la URL del repositorio en Jenkins (ver
> [README_PIPELINE.md](README_PIPELINE.md) paso 5.3).

---

## 15. Mantenimiento

### 15.1. Monitorización Rápida

```bash
# Estado de todos los pods
kubectl get pods -n project

# Logs de la API
kubectl logs -f deployment/api -n project

# Logs del Worker
kubectl logs -f deployment/worker -n project

# Uso de recursos
kubectl top pods -n project

# Estado de Docker containers (infraestructura)
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

### 15.2. Backups de MongoDB

```bash
# Backup
docker exec mongodb mongodump -u admin -p TU_PASSWORD --authenticationDatabase admin \
  --archive=/tmp/backup-$(date +%Y%m%d).gz --gzip

docker cp mongodb:/tmp/backup-$(date +%Y%m%d).gz /opt/backups/

# Restore
docker cp /opt/backups/backup-20260212.gz mongodb:/tmp/
docker exec mongodb mongorestore -u admin -p TU_PASSWORD --authenticationDatabase admin \
  --archive=/tmp/backup-20260212.gz --gzip --drop
```

### 15.3. Actualizar la Aplicación

Con Jenkins configurado, simplemente haz push a `main` y el pipeline se encarga de todo.

Para actualizar manualmente:

```bash
# Rebuild y push
docker build -t server:8085/project/api:v2 -f src/Project.Api/Dockerfile .
docker push server:8085/project/api:v2

# Actualizar deployment
kubectl set image deployment/api api=server:8085/project/api:v2 -n project
kubectl rollout status deployment/api -n project
```

### 15.4. Reiniciar Servicios

```bash
# Reiniciar un deployment de K8s
kubectl rollout restart deployment/api -n project

# Reiniciar infraestructura Docker
cd /opt/mongodb && docker compose restart
cd /opt/rabbitmq && docker compose restart
cd /opt/redis && docker compose restart

# Reiniciar Harbor
sudo systemctl restart harbor

# Reiniciar K3s
sudo systemctl restart k3s
```

### 15.5. Limpieza de Espacio en Disco

```bash
# Limpiar imágenes Docker no usadas
docker system prune -a --volumes -f

# Limpiar builds viejos de Jenkins
# (configurar en Jenkins → Manage → System → discard old builds)
```

---

## 16. Solución de Problemas

### Los pods no arrancan — ImagePullBackOff

K3s no puede descargar imágenes de Harbor:

```bash
kubectl describe pod <pod-name> -n project
```

**Solución:** Verifica que K3s tenga configurado el registro inseguro (paso 4.4) y que Harbor esté corriendo.

```bash
# Comprobar que Harbor responde
curl http://server:8085/api/v2.0/health

# Comprobar registries.yaml
cat /etc/rancher/k3s/registries.yaml

# Reiniciar K3s después de cambiar registries.yaml
sudo systemctl restart k3s
```

### MongoDB: Connection Refused desde K3s

Los pods de K3s no pueden conectar a MongoDB en Docker:

```bash
# Verificar que MongoDB escucha en todas las interfaces
docker exec mongodb mongosh --eval "db.adminCommand('getCmdLineOpts')"

# Verificar conectividad desde un pod
kubectl run -it --rm debug --image=busybox -n project -- telnet 192.168.1.100 27017
```

**Solución:** Asegúrate de usar la IP real del host (no `localhost`) en las connection strings de K8s secrets.

### Jenkins: Permission Denied para Docker

```
Got permission denied while trying to connect to the Docker daemon socket
```

**Solución:**

```bash
sudo usermod -aG docker jenkins
sudo systemctl restart jenkins
```

### SonarQube: max virtual memory areas vm.max_map_count too low

```bash
sudo sysctl -w vm.max_map_count=524288
echo "vm.max_map_count=524288" | sudo tee -a /etc/sysctl.conf
```

### RabbitMQ: MassTransit no conecta

Verifica que RabbitMQ esté corriendo y accesible:

```bash
# Desde el servidor
curl -u admin:TU_PASSWORD http://server:15672/api/overview

# Verificar que el puerto AMQP está abierto
telnet server 5672
```

Si ves errores de "Quorum Queue" en los logs del Worker, es normal en el primer arranque:
MassTransit crea las colas automáticamente.

### Pods en CrashLoopBackOff

Revisa los logs del pod:

```bash
kubectl logs <pod-name> -n project --previous
```

Causas comunes:
- Connection strings incorrectas en secrets
- MongoDB/RabbitMQ/Redis no accesibles
- JWT Secret demasiado corto (mínimo 32 caracteres)

### Grafana: No hay datos

1. Verifica que OTel Collector esté corriendo: `kubectl logs deployment/otel-collector -n project`
2. Verifica que Prometheus esté scrapeando: accede a `http://server:30030` → Explore → Prometheus → busca `up`
3. Verifica que la aplicación envía telemetría: revisa los logs de la API buscando "OpenTelemetry"

---

## Resumen de Orden de Instalación

```
1.  Ubuntu Server 24.04 LTS + paquetes base
2.  IP fija + hostname "server"
3.  Docker + Docker Compose
4.  K3s + Nginx Ingress Controller
5.  Harbor (registro de contenedores)
6.  Jenkins + .NET SDK + Trivy + SonarScanner
7.  SonarQube + PostgreSQL
8.  MongoDB
9.  RabbitMQ
10. Redis
11. kubectl apply → observabilidad (Loki, Tempo, Prometheus, OTel, FluentBit, Grafana)
12. kubectl apply → secrets, configmap, deployments de la aplicación
13. Configurar hosts en PC de desarrollo
14. Abrir navegador → verificar todos los servicios
```

Una vez completado, tendrás un entorno profesional de microservicios con:

- **Registro privado** de contenedores (Harbor)
- **CI/CD** completo con análisis estático y escaneo de seguridad (Jenkins + SonarQube + Trivy)
- **Orquestación** Kubernetes (K3s)
- **Base de datos** documental (MongoDB)
- **Mensajería** asíncrona con Outbox pattern (RabbitMQ + MassTransit)
- **Caché** distribuida (Redis)
- **Observabilidad** completa: métricas (Prometheus), logs (Loki + FluentBit), traces (Tempo + OTel), dashboards (Grafana)