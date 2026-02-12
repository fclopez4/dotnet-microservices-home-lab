# Project Template - .NET 10 Cloud-Native Architecture

Plantilla de arquitectura empresarial cloud-native lista para produccion. Diseñada para servir como scaffolding de nuevos proyectos: renombra `Project` por el nombre de tu dominio, configura tus secretos y empieza a desarrollar logica de negocio.

### Despliegue automatizado con Claude Code

Este repositorio incluye un archivo [CLAUDE.md](CLAUDE.md) que actua como **playbook de despliegue automatizado**. Si tienes [Claude Code](https://claude.com/claude-code) (la CLI de Anthropic), puedes desplegar toda la infraestructura del home lab en un servidor Ubuntu sin intervencion manual:

1. Clona este repositorio en tu servidor Ubuntu.
2. Abre Claude Code en el directorio del proyecto.
3. Dile: **"Despliega toda la infraestructura"**.
4. Claude te pedira solo 3 datos: la IP del servidor, una contrasena maestra y confirmacion de sudo.
5. Claude ejecutara los 15 pasos del playbook: Docker, K3s, Harbor, GitLab, Jenkins, SonarQube, MongoDB, RabbitMQ, Redis, observabilidad (Prometheus, Grafana, Loki, Tempo), despliegue de la app, y configuracion de la pipeline CI/CD.

Tambien puedes pedirle **"Verifica la infraestructura"** para comprobar el estado de los 12 servicios, o **"Repara Jenkins"** (o cualquier otro componente) si algo falla. El objetivo es que el programador solo se preocupe de las reglas de negocio.

---

## Stack tecnologico

| Capa | Tecnologia | Version |
|------|-----------|---------|
| Runtime | .NET | 10.0 (C# 13) |
| API | ASP.NET Core Minimal APIs | 10.0 |
| CQRS | MediatR | 14.x |
| Validacion | FluentValidation | 12.x |
| Bus de mensajes | MassTransit + RabbitMQ | 8.4 / Quorum Queues |
| Outbox pattern | MassTransit.MongoDb | 8.4 |
| Resiliencia | Polly | 8.x |
| Base de datos | MongoDB | Driver 3.6 |
| Cache distribuida | Redis (StackExchange) | 10.x |
| Autenticacion | JWT Bearer + Refresh Tokens + BCrypt | - |
| Logging | Serilog | 10.x |
| Trazas distribuidas | OpenTelemetry → OTLP Collector → Tempo | 1.15 |
| Metricas | OpenTelemetry → Prometheus → Grafana | 1.15 |
| Logs centralizados | FluentBit → Loki → Grafana | - |
| Alertas | Prometheus AlertManager | - |
| Error monitoring | Sentry | 5.x |
| Documentacion API | Scalar (OpenAPI) | 1.2 |
| Tests | xUnit + NSubstitute + FluentAssertions | - |
| Contenedores | Docker multi-stage | - |
| Orquestacion | Kubernetes (K3s) | - |
| Ingress | Nginx Ingress Controller + Cert Manager | - |
| CI/CD | Jenkins (9 stages) | - |
| Calidad | SonarQube | - |
| Seguridad | Trivy | - |
| Registro | Harbor | - |
| Frontend | Ionic/Angular + Nginx (4 replicas) | - |

---

## Arquitectura Cloud-Native

```
Hexagonal Architecture (Ports & Adapters) + DDD + CQRS + Event-Driven
```

![Arquitectura Cloud-Native de Microservicios .NET sobre Kubernetes](AppMicroserviciosNET.jpg)

### Flujo de una peticion

```
HTTPS Request → Ingress Controller (TLS + rate limit)
  → Frontend (Nginx) o API directa
    → JWT Validation (access token / refresh token)
      → MediatR Send(Command/Query)
        → ValidationBehavior (FluentValidation)
          → Handler (Application)
            → Port/Interface (Domain)
              → Implementation (Infrastructure)
                → MongoDB (Polly retry + circuit breaker)
                → Redis (cache distribuida)
```

### Flujo de mensajeria asincrona (MassTransit)

```
API: EnqueueEmailHandler
  → IPublishEndpoint.Publish(SendEmailMessage)
    → MongoDB Outbox (transaccional)
      → Background Dispatcher
        → RabbitMQ (Quorum Queue: email_queue)
          → SendEmailConsumer (Worker)
            → MailKit SMTP
              ↓ (si falla tras 3 reintentos)
            Dead Letter Queue
```

---

## Estructura del proyecto

```
Project.slnx
├── src/
│   ├── Project.Domain/                 # Nucleo - sin dependencias externas
│   │   ├── Entities/                   # User, EmailMessage, RefreshToken
│   │   ├── ValueObjects/               # Email (inmutable, con validacion)
│   │   ├── Enums/                      # UserRole (Guest, User, Worker, Admin)
│   │   ├── Exceptions/                 # DomainException
│   │   └── Ports/                      # IUserRepository, IEmailQueue, IPasswordHasher,
│   │                                   # IRefreshTokenRepository
│   │
│   ├── Project.Application/            # Casos de uso
│   │   ├── Commands/                   # RegisterUser, Login, RefreshToken, EnqueueEmail
│   │   ├── Queries/                    # GetUserById
│   │   ├── DTOs/                       # Objetos de transferencia
│   │   ├── Interfaces/                 # IJwtService
│   │   ├── Behaviors/                  # ValidationBehavior (pipeline MediatR)
│   │   ├── Validators/                 # Reglas FluentValidation
│   │   └── DependencyInjection.cs
│   │
│   ├── Project.Infrastructure/         # Implementaciones tecnicas
│   │   ├── Persistence/
│   │   │   ├── MongoDbContext.cs        # Conexion y colecciones
│   │   │   ├── Repositories/           # UserRepository, RefreshTokenRepository
│   │   │   └── Configuration/          # BSON mappings, serializers
│   │   ├── Messaging/
│   │   │   ├── MassTransitEmailQueue.cs    # IEmailQueue via MassTransit
│   │   │   ├── Contracts/                  # SendEmailMessage (message contract)
│   │   │   └── Consumers/                  # SendEmailConsumer
│   │   ├── Security/                   # JwtService (+ refresh), BCryptPasswordHasher
│   │   ├── Resilience/                 # Polly: retry + circuit breaker (MongoDB)
│   │   ├── Migrations/                 # Sistema de migraciones MongoDB
│   │   └── DependencyInjection.cs      # MassTransit, Redis, Polly, repos, security
│   │
│   ├── Project.Api/                    # Punto de entrada HTTP
│   │   ├── Endpoints/                  # Auth (+ refresh), Users, Email
│   │   ├── Program.cs                  # Sentry, OTLP, JWT, middleware
│   │   ├── Dockerfile
│   │   └── appsettings.*.json          # Development, Staging, Production
│   │
│   └── Project.Worker/                 # MassTransit consumer host
│       ├── Program.cs                  # Sentry, OTLP, MassTransit bus
│       ├── Dockerfile
│       └── appsettings.*.json
│
├── tests/
│   ├── Project.Domain.Tests/           # Tests de entidades y value objects
│   ├── Project.Application.Tests/      # Tests de handlers con mocks
│   └── Project.Integration.Tests/      # Tests de endpoints (requiere infra)
│
├── frontend/                           # Ionic/Angular (placeholder)
│   ├── Dockerfile                      # Node build + Nginx
│   └── nginx.conf                      # Reverse proxy a API
│
├── k8s/                                # Manifiestos Kubernetes
│   ├── namespace.yaml
│   ├── configmap.yaml
│   ├── secrets.yaml                    # MongoDB, RabbitMQ, Redis, JWT, Sentry DSN
│   ├── ingress.yaml                    # Nginx Ingress + TLS + Cert Manager
│   ├── api-deployment.yaml             # 2 replicas + Service NodePort:30080
│   ├── worker-deployment.yaml          # 1 replica
│   ├── frontend-deployment.yaml        # 4 replicas + Service NodePort:30081
│   ├── redis-deployment.yaml           # Redis cache
│   └── observability/
│       ├── otel-collector.yaml         # OpenTelemetry Collector (OTLP → Tempo/Prometheus)
│       ├── fluentbit.yaml              # Log collection → Loki
│       ├── prometheus.yaml             # Metricas + AlertManager
│       ├── loki.yaml                   # Agregacion de logs
│       ├── tempo.yaml                  # Trazas distribuidas
│       └── grafana.yaml                # Dashboards (Prometheus + Loki + Tempo)
│
├── Jenkinsfile                         # Pipeline CI/CD (9 stages)
├── docker-compose.yml                  # MongoDB + RabbitMQ + Redis (desarrollo local)
├── CLAUDE.md                           # Playbook automatizado para que Claude despliegue todo
├── INFRASTRUCTURE.md                   # Guia de despliegue del home lab completo
├── README_PIPELINE.md                  # Pipeline CI/CD en Jenkins + integracion GitLab
└── .vscode/                            # Debug con Edge + HTTPS
```

---

## Puesta en marcha local

### Requisitos previos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (para MongoDB, RabbitMQ y Redis)
- [VS Code](https://code.visualstudio.com/) con extension C# Dev Kit (recomendado)

### 1. Levantar infraestructura

```bash
docker compose up -d
```

Esto levanta MongoDB, RabbitMQ y Redis usando el `docker-compose.yml` del proyecto.

Consolas:
- RabbitMQ: http://localhost:15672 (admin/dev_password)
- Redis CLI: `docker exec -it redis redis-cli`

### 2. Configurar secretos locales

Los archivos `appsettings.json` contienen placeholders (`CONFIGURE_IN_USER_SECRETS`). Usa **user-secrets** para sobreescribirlos sin exponer credenciales en el repositorio:

```bash
# Inicializar user-secrets en los proyectos
dotnet user-secrets init --project src/Project.Api
dotnet user-secrets init --project src/Project.Worker

# Configurar la API
dotnet user-secrets set "ConnectionStrings:MongoDB" "mongodb://admin:dev_password@localhost:27017" --project src/Project.Api
dotnet user-secrets set "ConnectionStrings:RabbitMQ" "amqp://admin:dev_password@localhost:5672" --project src/Project.Api
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project src/Project.Api
dotnet user-secrets set "Jwt:Secret" "MiClaveSecretaLocalDe32CaracteresMinimo!" --project src/Project.Api

# Configurar el Worker
dotnet user-secrets set "ConnectionStrings:MongoDB" "mongodb://admin:dev_password@localhost:27017" --project src/Project.Worker
dotnet user-secrets set "ConnectionStrings:RabbitMQ" "amqp://admin:dev_password@localhost:5672" --project src/Project.Worker
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project src/Project.Worker
```

Los secretos se almacenan en `%APPDATA%\Microsoft\UserSecrets\` y nunca se suben al repositorio.

### 3. Compilar y ejecutar tests

```bash
dotnet restore
dotnet build
dotnet test
```

### 4. Ejecutar la API

```bash
dotnet run --project src/Project.Api
```

La API arranca en:
- **HTTPS**: https://localhost:7001
- **HTTP**: http://localhost:5001
- **Documentacion**: https://localhost:7001/scalar/v1

Si usas VS Code, pulsa `F5` para lanzar con el debugger (abre Edge automaticamente en Scalar).

### 5. Ejecutar el Worker

En otra terminal:

```bash
dotnet run --project src/Project.Worker
```

El Worker arranca MassTransit y comienza a consumir la cola `email_queue` automaticamente.

---

## Endpoints de la API

| Metodo | Ruta | Auth | Descripcion |
|--------|------|------|-------------|
| POST | `/api/auth/register` | No | Registrar usuario |
| POST | `/api/auth/login` | No | Login, devuelve JWT + refresh token |
| POST | `/api/auth/refresh` | No | Renovar tokens con refresh token |
| POST | `/api/auth/guest` | No | Sesion de invitado |
| GET | `/api/users/{id}` | Si | Obtener usuario por ID |
| GET | `/api/users/admin/dashboard` | Admin | Panel de administracion |
| POST | `/api/email/send` | Si | Encolar email via MassTransit |
| GET | `/health` | No | Health check |

### Ejemplo: flujo completo de autenticacion

```bash
# 1. Registrar usuario
curl -X POST https://localhost:7001/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"usuario1","email":"usuario1@example.com","password":"Password123!"}'

# 2. Login → recibe access token + refresh token
curl -X POST https://localhost:7001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"usuario1","password":"Password123!"}'
# Respuesta: { "token": "eyJhbG...", "username": "usuario1", "role": "User", "refreshToken": "abc123..." }

# 3. Usar access token en endpoints protegidos
curl https://localhost:7001/api/users/{id} \
  -H "Authorization: Bearer eyJhbG..."

# 4. Cuando el access token expire, renovar con refresh token
curl -X POST https://localhost:7001/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"accessToken":"eyJhbG...expirado","refreshToken":"abc123..."}'
# Respuesta: nuevo access token + nuevo refresh token
```

---

## Mensajeria con MassTransit

La plantilla usa **MassTransit** como abstraccion sobre RabbitMQ, proporcionando:

- **Quorum Queues**: Colas replicadas para alta disponibilidad
- **Outbox Pattern (MongoDB)**: Garantiza que los mensajes se publican aunque RabbitMQ este caido
- **Background Dispatcher**: Publica mensajes pendientes del outbox automaticamente
- **Dead Letter Queue**: Mensajes fallidos se envian a DLQ tras 3 reintentos
- **Consumers tipados**: Cada mensaje tiene un consumer fuertemente tipado

### Añadir un nuevo tipo de mensaje

```csharp
// 1. Contrato en Infrastructure/Messaging/Contracts/
public record OrderCreatedMessage(string OrderId, string UserId, decimal Total);

// 2. Consumer en Infrastructure/Messaging/Consumers/
public class OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreatedMessage>
{
    public Task Consume(ConsumeContext<OrderCreatedMessage> context)
    {
        logger.LogInformation("Order {OrderId} created for {Total}",
            context.Message.OrderId, context.Message.Total);
        return Task.CompletedTask;
    }
}

// 3. Registrar consumer en DependencyInjection.cs
bus.AddConsumer<OrderCreatedConsumer>();
```

---

## Cache distribuida (Redis)

Redis esta disponible como `IDistributedCache` en toda la aplicacion. Si Redis no esta configurado, se usa cache en memoria como fallback.

```csharp
// Inyectar en cualquier handler o servicio
public class GetProductHandler(IDistributedCache cache, IProductRepository repo)
{
    public async Task<ProductDto> Handle(GetProductQuery request, CancellationToken ct)
    {
        var cacheKey = $"product:{request.Id}";
        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<ProductDto>(cached)!;

        var product = await repo.GetByIdAsync(request.Id, ct);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(product),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, ct);
        return product;
    }
}
```

---

## Observabilidad

### Arquitectura de observabilidad

```
App (.NET) → OTLP (gRPC:4317) → OTel Collector
                                     ├── Traces  → Tempo     → Grafana
                                     └── Metrics → Prometheus → Grafana

App (.NET) → stdout → FluentBit → Loki → Grafana

App (.NET) → Sentry SDK → Sentry (SaaS) → Alertas de errores
```

### Logs estructurados (Serilog)

```
[14:32:01 INF] Project.Api.Endpoints.AuthEndpoints
  User "usuario1" logged in successfully
```

### Trazas distribuidas (OpenTelemetry → Tempo)

Instrumentacion automatica de:
- Peticiones HTTP entrantes (ASP.NET Core)
- Peticiones HTTP salientes (HttpClient)
- Exportacion via OTLP al OTel Collector

### Metricas (OpenTelemetry → Prometheus)

El OTel Collector expone metricas en formato Prometheus que son scrapeadas automaticamente.

### Error monitoring (Sentry)

Sentry captura excepciones no manejadas y performance traces. Configura el DSN via user-secrets o K8s secrets.

### Grafana

Acceso al dashboard: `http://server:30030` (NodePort)
Datasources preconfigurados: Prometheus, Loki, Tempo.

---

## Adaptar a tu dominio

Esta plantilla esta pensada para renombrarse y extenderse. Pasos recomendados:

### 1. Renombrar el proyecto

Reemplaza `Project` por el nombre de tu aplicacion en:
- Nombre del `.slnx`
- Nombres de carpetas (`src/Project.*` → `src/MiApp.*`)
- Nombres de `.csproj`
- Referencias en `using`, `namespace` y configuraciones
- Dockerfiles, Jenkinsfile, k8s/

### 2. Definir tus entidades de dominio

Modifica o reemplaza las entidades en `Project.Domain/Entities/`:

```csharp
public class Producto
{
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    public string Nombre { get; private set; }
    public decimal Precio { get; private set; }
    // ...factory methods, comportamiento de dominio
}
```

Define el puerto (interfaz) en `Project.Domain/Ports/`:

```csharp
public interface IProductoRepository
{
    Task<Producto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task CreateAsync(Producto producto, CancellationToken ct = default);
}
```

### 3. Crear Commands/Queries

```csharp
public record CrearProductoCommand(string Nombre, decimal Precio) : IRequest<ProductoDto>;

public class CrearProductoHandler(IProductoRepository repo) : IRequestHandler<CrearProductoCommand, ProductoDto>
{
    public async Task<ProductoDto> Handle(CrearProductoCommand request, CancellationToken ct)
    {
        var producto = Producto.Create(request.Nombre, request.Precio);
        await repo.CreateAsync(producto, ct);
        return new ProductoDto(producto.Id, producto.Nombre, producto.Precio);
    }
}
```

### 4. Implementar el repositorio

```csharp
public class ProductoRepository(MongoDbContext context, ResiliencePipelineProvider<string> resilience)
    : IProductoRepository
{
    private readonly IMongoCollection<Producto> _collection = context.Database.GetCollection<Producto>("productos");
    private readonly ResiliencePipeline _pipeline = resilience.GetPipeline(ResilienceExtensions.MongoDbPipeline);

    public async Task<Producto?> GetByIdAsync(string id, CancellationToken ct) =>
        await _pipeline.ExecuteAsync(async token =>
            await _collection.Find(p => p.Id == id).FirstOrDefaultAsync(token), ct);
}
```

### 5. Exponer el endpoint

```csharp
public static class ProductoEndpoints
{
    public static void MapProductoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/productos").WithTags("Productos");

        group.MapPost("/", async (CrearProductoCommand cmd, IMediator mediator) =>
        {
            var result = await mediator.Send(cmd);
            return Results.Created($"/api/productos/{result.Id}", result);
        });
    }
}
```

### 6. Registrar servicios y crear migracion

```csharp
// En Infrastructure/DependencyInjection.cs
services.AddScoped<IProductoRepository, ProductoRepository>();
```

Las migraciones se ejecutan automaticamente al arrancar la API y el Worker.

---

## CI/CD Pipeline (Jenkins)

El `Jenkinsfile` define 9 stages. Documentacion detallada en [README_PIPELINE.md](README_PIPELINE.md).

```
1. Checkout           → Clona el repositorio
2. Restore & Build    → dotnet restore + build Release
3. Test               → dotnet test con code coverage (opencover)
4. SonarQube          → Analisis de calidad de codigo
5. Docker Build       → Construye 3 imagenes (api, worker, frontend)
6. Trivy Scan         → Escaneo de vulnerabilidades en contenedores
7. Push to Harbor     → Sube imagenes al registro privado
8. Deploy to K3s      → Aplica manifiestos (app + observabilidad) y actualiza imagenes
9. Verify Deployment  → Health checks de API, Frontend, Worker y Grafana
```

---

## Despliegue en Kubernetes

### Topologia

```
Namespace: project
├── api (Deployment, 2 replicas) + Service :30080
├── worker (Deployment, 1 replica)
├── frontend (Deployment, 4 replicas) + Service :30081
├── redis (Deployment, 1 replica) + Service
├── ingress (Nginx Ingress + TLS)
└── observability/
    ├── otel-collector (traces + metrics pipeline)
    ├── fluentbit (DaemonSet, log collection)
    ├── prometheus + alertmanager
    ├── loki (log aggregation)
    ├── tempo (distributed tracing)
    └── grafana + Service :30030
```

### Secrets requeridos

```yaml
stringData:
  mongodb-connection: "mongodb://admin:TU_PASSWORD@mongodb:27017"
  rabbitmq-connection: "amqp://admin:TU_PASSWORD@rabbitmq:5672"
  redis-connection: "redis:6379"
  jwt-secret: "TU_JWT_SECRET_DE_32_CARACTERES_MINIMO"
  sentry-dsn: "https://TU_SENTRY_DSN"
```

---

## Tests

```bash
# Ejecutar todos los tests
dotnet test

# Solo tests de dominio
dotnet test tests/Project.Domain.Tests

# Solo tests de aplicacion
dotnet test tests/Project.Application.Tests

# Con cobertura
dotnet test --collect:"XPlat Code Coverage"
```

| Proyecto | Tipo | Que valida |
|----------|------|-----------|
| Domain.Tests | Unitarios | Email value object, User entity, reglas de dominio |
| Application.Tests | Unitarios con mocks | Handlers, logica de negocio, validaciones |
| Integration.Tests | Integracion | Endpoints HTTP completos (requiere MongoDB + RabbitMQ) |

---

## Documentacion adicional

| Documento | Contenido |
|-----------|-----------|
| [README.md](README.md) | Vision global del proyecto (este archivo) |
| [README_PIPELINE.md](README_PIPELINE.md) | Pipeline CI/CD en Jenkins: stages, configuracion, integracion con GitLab |
| [INFRASTRUCTURE.md](INFRASTRUCTURE.md) | Guia paso a paso para desplegar el home lab completo en Ubuntu Server |
| [CLAUDE.md](CLAUDE.md) | Playbook automatizado: Claude despliega toda la infraestructura solo |

---

## Licencia

Este proyecto esta licenciado bajo [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)](https://creativecommons.org/licenses/by-nc-sa/4.0/).

- **BY** — Debes dar credito al autor original.
- **NC** — No puedes usar el material con fines comerciales.
- **SA** — Si modificas o construyes sobre este material, debes distribuirlo bajo la misma licencia.

Consulta el archivo [LICENSE](LICENSE) para mas detalles.
