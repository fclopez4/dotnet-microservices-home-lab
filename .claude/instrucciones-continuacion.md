# Instrucciones de Continuacion para Claude

Si la sesion se interrumpe, usa este archivo para retomar el trabajo.

## Estado del proyecto

### Arquitectura
- .NET 10, Arquitectura Hexagonal DDD
- Capas: Domain > Application > Infrastructure > Api/Worker
- Solucion: `Project.slnx`
- SDK: `global.json` fija net10.0 con rollForward latestFeature

### Convenciones de codigo
- Primary constructors para DI
- Records para Commands/DTOs
- File-scoped namespaces
- Factory methods en entidades (`User.Create(...)`)
- Minimal APIs con endpoint groups (`MapGroup("/api/...")`)

### Requisitos implementados

| # | Requisito | Estado | Archivos clave |
|---|-----------|--------|----------------|
| 1 | Jenkins: build, test, SonarQube | HECHO | `Jenkinsfile` |
| 2 | Docker: front Ionic, API, Worker | HECHO | `src/Project.Api/Dockerfile`, `src/Project.Worker/Dockerfile`, `frontend/Dockerfile` |
| 3 | Trivy scan 3 imagenes | HECHO | `Jenkinsfile` stage Trivy |
| 4 | Push a Harbor 3 imagenes | HECHO | `Jenkinsfile` stage Push |
| 5 | Migraciones MongoDB | HECHO | `src/Project.Infrastructure/Migrations/` |
| 6 | Deploy a K3s | HECHO | `k8s/*.yaml`, `Jenkinsfile` stage Deploy |
| 7 | Verificar despliegue | HECHO | `Jenkinsfile` stage Verify |
| 8 | AppSettings por entorno | HECHO | `appsettings.Staging.json`, `appsettings.Production.json` |
| 9 | RabbitMQ con resiliencia | HECHO | `src/Project.Infrastructure/Messaging/RabbitMqEmailQueue.cs` |
| 10 | Polly + MediatR + FluentValidation | HECHO | `Behaviors/ValidationBehavior.cs`, `Validators/`, `Resilience/` |
| 11 | OpenTelemetry + Serilog | HECHO | `Program.cs` (Api y Worker) |

### Paquetes NuGet anadidos

**Application:**
- FluentValidation.DependencyInjectionExtensions

**Infrastructure:**
- Microsoft.Extensions.Resilience
- Polly

**API:**
- Serilog.AspNetCore
- OpenTelemetry.Extensions.Hosting
- OpenTelemetry.Instrumentation.AspNetCore
- OpenTelemetry.Instrumentation.Http
- OpenTelemetry.Exporter.Console
- Scalar.AspNetCore

**Worker:**
- Serilog.Extensions.Hosting
- Serilog.Sinks.Console
- Serilog.Sinks.File
- OpenTelemetry.Extensions.Hosting
- OpenTelemetry.Instrumentation.Http
- OpenTelemetry.Exporter.Console

### Archivos creados en esta sesion

```
src/Project.Application/Behaviors/ValidationBehavior.cs
src/Project.Application/Validators/RegisterUserCommandValidator.cs
src/Project.Application/Validators/EnqueueEmailCommandValidator.cs
src/Project.Infrastructure/Resilience/ResilienceExtensions.cs
src/Project.Infrastructure/Migrations/IMigration.cs
src/Project.Infrastructure/Migrations/MigrationRecord.cs
src/Project.Infrastructure/Migrations/MigrationRunner.cs
src/Project.Infrastructure/Migrations/M001_CreateIndexes.cs
src/Project.Api/appsettings.Staging.json
src/Project.Api/appsettings.Production.json
src/Project.Worker/appsettings.Staging.json
src/Project.Worker/appsettings.Production.json
frontend/package.json
frontend/ionic.config.json
frontend/src/index.html
frontend/nginx.conf
frontend/Dockerfile
k8s/frontend-deployment.yaml
INSTRUCCIONES-MANUALES.md
```

### Archivos modificados en esta sesion

```
src/Project.Application/Project.Application.csproj (FluentValidation)
src/Project.Application/DependencyInjection.cs (validators + behavior)
src/Project.Infrastructure/Project.Infrastructure.csproj (Polly, Resilience)
src/Project.Infrastructure/DependencyInjection.cs (Polly pipelines, IConnectionFactory, MigrationRunner)
src/Project.Infrastructure/Messaging/RabbitMqEmailQueue.cs (refactor completo)
src/Project.Infrastructure/Persistence/MongoDbContext.cs (+Database property)
src/Project.Infrastructure/Persistence/Repositories/UserRepository.cs (+Polly wrap)
src/Project.Api/Project.Api.csproj (Serilog, OpenTelemetry, Scalar)
src/Project.Api/Program.cs (Serilog, OpenTelemetry, exception handler, migrations)
src/Project.Api/Dockerfile (curl -> wget)
src/Project.Worker/Project.Worker.csproj (Serilog, OpenTelemetry)
src/Project.Worker/Program.cs (Serilog, OpenTelemetry, migrations)
k8s/configmap.yaml (+ASPNETCORE_ENVIRONMENT, +DOTNET_ENVIRONMENT)
k8s/api-deployment.yaml (env vars desde configmap)
k8s/worker-deployment.yaml (env vars desde configmap)
Jenkinsfile (frontend stages, verify deployment, opencover format)
.gitignore (+frontend, +logs)
.vscode/launch.json (Scalar URL)
.vscode/tasks.json (creado)
```

### Pendiente / Mejoras futuras
- Crear LoginCommandValidator (no se creo, solo RegisterUser y EnqueueEmail)
- Health checks con AspNetCore.HealthChecks.RabbitMQ/MongoDb (paquetes no anadidos)
- OTLP exporter para enviar trazas a un collector en produccion (ahora solo Console)
- Serilog con configuracion desde appsettings (ahora hardcoded en Program.cs)
- Tests de integracion habilitados (actualmente comentados, requieren MongoDB/RabbitMQ)
- Crear proyecto Ionic real (actualmente es placeholder)