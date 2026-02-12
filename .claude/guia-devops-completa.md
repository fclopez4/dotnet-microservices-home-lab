# Guía DevOps Completa: .NET 10 + CI/CD en Server

## Mapa General del Proyecto

```
┌─────────────┐    ┌──────────┐    ┌──────────────────────────────────────────────────┐    ┌─────────┐    ┌──────┐
│  Tu PC      │    │  GitLab  │    │              Jenkins Pipeline                     │    │ Harbor  │    │ K3s  │
│  (VSCode +  │───▶│  :8081   │───▶│ Build → Test → Sonar → Docker → Trivy → Push     │───▶│ :8085   │───▶│Deploy│
│  ClaudeCode)│    │          │    │                                                    │    │         │    │      │
└─────────────┘    └──────────┘    └──────────────────────────────────────────────────┘    └─────────┘    └──────┘
```

---

## FASE 1: Infraestructura en Server

### 1.1 Instalar SonarQube (Docker)

```bash
# Crear red y volúmenes
docker network create sonar-net

docker volume create sonarqube_data
docker volume create sonarqube_logs
docker volume create sonarqube_extensions
docker volume create postgresql_data

# PostgreSQL para SonarQube
docker run -d --name sonarqube-db \
  --network sonar-net \
  --restart unless-stopped \
  -e POSTGRES_USER=sonar \
  -e POSTGRES_PASSWORD=sonar_password_segura \
  -e POSTGRES_DB=sonarqube \
  -v postgresql_data:/var/lib/postgresql/data \
  postgres:16-alpine

# SonarQube
docker run -d --name sonarqube \
  --network sonar-net \
  --restart unless-stopped \
  -p 9000:9000 \
  -e SONAR_JDBC_URL=jdbc:postgresql://sonarqube-db:5432/sonarqube \
  -e SONAR_JDBC_USERNAME=sonar \
  -e SONAR_JDBC_PASSWORD=sonar_password_segura \
  -v sonarqube_data:/opt/sonarqube/data \
  -v sonarqube_extensions:/opt/sonarqube/extensions \
  -v sonarqube_logs:/opt/sonarqube/logs \
  sonarqube:lts-community

# Requisito del kernel para SonarQube (ElasticSearch)
sysctl -w vm.max_map_count=524288
echo "vm.max_map_count=524288" >> /etc/sysctl.conf
```

**Configurar SonarQube:**
1. Accede a `http://server:9000` (usuario: `admin`, contraseña: `admin`)
2. Cambia la contraseña
3. Ve a **Administration → Marketplace** e instala el plugin "SonarScanner for .NET"
4. Crea un proyecto: **Projects → Create Project → Manually**
   - Project key: `mi-proyecto`
   - Display name: `Mi Proyecto`
5. Genera un token en **My Account → Security → Generate Token** → guárdalo

### 1.2 Instalar Trivy (en Server, para Jenkins)

```bash
# Instalar Trivy en el host (Jenkins lo usará directamente)
sudo apt-get install -y wget apt-transport-https gnupg lsb-release
wget -qO - https://aquasecurity.github.io/trivy-repo/deb/public.key | gpg --dearmor | sudo tee /usr/share/keyrings/trivy.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/trivy.gpg] https://aquasecurity.github.io/trivy-repo/deb $(lsb_release -sc) main" | sudo tee /etc/apt/sources.list.d/trivy.list
sudo apt-get update
sudo apt-get install -y trivy

# Verificar
trivy --version
```

### 1.3 Instalar MongoDB y RabbitMQ en K3s

```bash
# Crear namespace
kubectl create namespace infrastructure

# --- MongoDB ---
cat <<'EOF' | kubectl apply -f -
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mongo-pvc
  namespace: infrastructure
spec:
  accessModes: [ReadWriteOnce]
  resources:
    requests:
      storage: 10Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mongodb
  namespace: infrastructure
spec:
  replicas: 1
  selector:
    matchLabels:
      app: mongodb
  template:
    metadata:
      labels:
        app: mongodb
    spec:
      containers:
      - name: mongodb
        image: mongo:7
        ports:
        - containerPort: 27017
        env:
        - name: MONGO_INITDB_ROOT_USERNAME
          value: "admin"
        - name: MONGO_INITDB_ROOT_PASSWORD
          value: "MongoPassword123!"
        volumeMounts:
        - name: mongo-data
          mountPath: /data/db
      volumes:
      - name: mongo-data
        persistentVolumeClaim:
          claimName: mongo-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: mongodb
  namespace: infrastructure
spec:
  type: NodePort
  selector:
    app: mongodb
  ports:
  - port: 27017
    targetPort: 27017
    nodePort: 30017
EOF

# --- RabbitMQ ---
cat <<'EOF' | kubectl apply -f -
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: rabbitmq-pvc
  namespace: infrastructure
spec:
  accessModes: [ReadWriteOnce]
  resources:
    requests:
      storage: 5Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rabbitmq
  namespace: infrastructure
spec:
  replicas: 1
  selector:
    matchLabels:
      app: rabbitmq
  template:
    metadata:
      labels:
        app: rabbitmq
    spec:
      containers:
      - name: rabbitmq
        image: rabbitmq:3-management
        ports:
        - containerPort: 5672
        - containerPort: 15672
        env:
        - name: RABBITMQ_DEFAULT_USER
          value: "admin"
        - name: RABBITMQ_DEFAULT_PASS
          value: "RabbitPassword123!"
        volumeMounts:
        - name: rabbitmq-data
          mountPath: /var/lib/rabbitmq
      volumes:
      - name: rabbitmq-data
        persistentVolumeClaim:
          claimName: rabbitmq-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: rabbitmq
  namespace: infrastructure
spec:
  type: NodePort
  selector:
    app: rabbitmq
  ports:
  - name: amqp
    port: 5672
    targetPort: 5672
    nodePort: 30672
  - name: management
    port: 15672
    targetPort: 15672
    nodePort: 31672
EOF
```

**Verificar:**
```bash
kubectl get pods -n infrastructure
# Esperar a que estén Running, luego:
# MongoDB: mongodb://admin:MongoPassword123!@server:30017
# RabbitMQ Management: http://server:31672 (admin/RabbitPassword123!)
# RabbitMQ AMQP: amqp://admin:RabbitPassword123!@server:30672
```

### 1.4 Configurar Harbor para el proyecto

```bash
# Accede a Harbor: https://server:8085
# 1. Login: admin / (tu contraseña)
# 2. Crea un proyecto: Projects → New Project → "mi-proyecto" (público o privado)

# Desde Server, hacer login en Harbor (para Jenkins)
docker login server:8085 -u admin -p TU_PASSWORD_HARBOR
```

### 1.5 Configurar Jenkins

Instalar plugins necesarios en Jenkins (`http://server:8086`):

1. **Manage Jenkins → Plugins → Available plugins**, instala:
   - `GitLab Plugin`
   - `Docker Pipeline`
   - `SonarQube Scanner`
   - `Kubernetes CLI Plugin`
   - `Pipeline Utility Steps`

2. **Manage Jenkins → Tools:**
   - Añadir instalación de .NET SDK 10 (o configurar la ruta si ya está instalado)
   - Configurar SonarQube Scanner

3. **Manage Jenkins → System → SonarQube servers:**
   - Name: `SonarQube`
   - URL: `http://server:9000`
   - Token: (el que generaste en SonarQube)

4. **Manage Jenkins → Credentials**, añadir:
   - `gitlab-credentials` (Username/Password para GitLab)
   - `harbor-credentials` (Username/Password para Harbor)
   - `sonarqube-token` (Secret text con el token de Sonar)
   - `kubeconfig` (Secret file con `/etc/rancher/k3s/k3s.yaml` - edita la IP de 127.0.0.1 a server)

---

## FASE 2: Proyecto .NET 10 — Arquitectura Hexagonal DDD

### 2.1 Estructura de la solución

```
MiProyecto/
├── src/
│   ├── MiProyecto.Api/                    # Capa API (Minimal API + Swagger + JWT)
│   │   ├── Program.cs
│   │   ├── Endpoints/
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── UserEndpoints.cs
│   │   │   └── ...
│   │   ├── Middleware/
│   │   │   └── ExceptionHandlingMiddleware.cs
│   │   ├── Configuration/
│   │   │   ├── JwtConfiguration.cs
│   │   │   ├── MongoConfiguration.cs
│   │   │   └── RabbitMqConfiguration.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   └── Dockerfile
│   │
│   ├── MiProyecto.Domain/                 # Capa de Dominio (puro, sin dependencias)
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   └── EmailMessage.cs
│   │   ├── ValueObjects/
│   │   │   ├── Email.cs
│   │   │   └── Role.cs
│   │   ├── Enums/
│   │   │   └── UserRole.cs               # Guest, User, Worker, Admin
│   │   ├── Exceptions/
│   │   │   └── DomainException.cs
│   │   └── Ports/
│   │       ├── IUserRepository.cs         # Puerto de salida
│   │       ├── IEmailQueue.cs             # Puerto de salida
│   │       └── IPasswordHasher.cs         # Puerto de salida
│   │
│   ├── MiProyecto.Application/            # Capa de Aplicación (casos de uso)
│   │   ├── Commands/
│   │   │   ├── RegisterUser/
│   │   │   │   ├── RegisterUserCommand.cs
│   │   │   │   └── RegisterUserHandler.cs
│   │   │   ├── Login/
│   │   │   │   ├── LoginCommand.cs
│   │   │   │   └── LoginHandler.cs
│   │   │   └── SendEmail/
│   │   │       ├── EnqueueEmailCommand.cs
│   │   │       └── EnqueueEmailHandler.cs
│   │   ├── Queries/
│   │   │   └── GetUserById/
│   │   │       ├── GetUserByIdQuery.cs
│   │   │       └── GetUserByIdHandler.cs
│   │   ├── DTOs/
│   │   │   ├── UserDto.cs
│   │   │   ├── LoginRequest.cs
│   │   │   └── LoginResponse.cs
│   │   ├── Interfaces/
│   │   │   ├── IJwtService.cs
│   │   │   └── IEmailService.cs
│   │   └── DependencyInjection.cs
│   │
│   ├── MiProyecto.Infrastructure/         # Capa de Infraestructura
│   │   ├── Persistence/
│   │   │   ├── MongoDbContext.cs
│   │   │   ├── Repositories/
│   │   │   │   └── UserRepository.cs      # Implementa IUserRepository
│   │   │   └── Mappings/
│   │   │       └── UserMapping.cs
│   │   ├── Messaging/
│   │   │   └── RabbitMqEmailQueue.cs      # Implementa IEmailQueue
│   │   ├── Security/
│   │   │   ├── JwtService.cs              # Implementa IJwtService
│   │   │   └── BCryptPasswordHasher.cs    # Implementa IPasswordHasher
│   │   ├── Email/
│   │   │   └── SmtpEmailService.cs        # Implementa IEmailService
│   │   └── DependencyInjection.cs
│   │
│   └── MiProyecto.Worker/                 # Worker Service (Background)
│       ├── Program.cs
│       ├── EmailWorker.cs                 # BackgroundService que consume RabbitMQ
│       ├── appsettings.json
│       └── Dockerfile
│
├── tests/
│   ├── MiProyecto.Domain.Tests/
│   ├── MiProyecto.Application.Tests/
│   └── MiProyecto.Integration.Tests/
│
├── frontend/                              # Ionic + Angular
│   ├── src/
│   │   ├── app/
│   │   │   ├── core/
│   │   │   │   ├── services/
│   │   │   │   │   ├── auth.service.ts
│   │   │   │   │   ├── api.service.ts
│   │   │   │   │   └── token.service.ts
│   │   │   │   ├── guards/
│   │   │   │   │   ├── auth.guard.ts
│   │   │   │   │   └── role.guard.ts
│   │   │   │   ├── interceptors/
│   │   │   │   │   └── jwt.interceptor.ts
│   │   │   │   └── models/
│   │   │   │       ├── user.model.ts
│   │   │   │       └── auth.model.ts
│   │   │   ├── pages/
│   │   │   │   ├── login/
│   │   │   │   ├── home/
│   │   │   │   ├── admin/
│   │   │   │   └── worker/
│   │   │   └── app-routing.module.ts
│   │   └── environments/
│   ├── Dockerfile
│   ├── ionic.config.json
│   └── angular.json
│
├── k8s/                                   # Manifests Kubernetes
│   ├── namespace.yaml
│   ├── api-deployment.yaml
│   ├── api-service.yaml
│   ├── worker-deployment.yaml
│   ├── frontend-deployment.yaml
│   ├── frontend-service.yaml
│   ├── configmap.yaml
│   └── secrets.yaml
│
├── docker-compose.yml                     # Para desarrollo local
├── Jenkinsfile                            # Pipeline CI/CD
├── MiProyecto.sln
├── .gitignore
├── Directory.Build.props                  # Configuración global .NET
└── global.json                            # Fijar versión .NET 10
```

### 2.2 Crear la solución (ejecutar en tu PC de desarrollo)

```bash
# Verificar .NET 10
dotnet --version  # Debe mostrar 10.0.x

# Crear directorio
mkdir MiProyecto && cd MiProyecto

# Fijar versión SDK
cat > global.json << 'EOF'
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
EOF

# Crear solución
dotnet new sln -n MiProyecto

# Crear proyectos
dotnet new classlib -n MiProyecto.Domain -o src/MiProyecto.Domain -f net10.0
dotnet new classlib -n MiProyecto.Application -o src/MiProyecto.Application -f net10.0
dotnet new classlib -n MiProyecto.Infrastructure -o src/MiProyecto.Infrastructure -f net10.0
dotnet new webapi -n MiProyecto.Api -o src/MiProyecto.Api -f net10.0 --use-minimal-apis
dotnet new worker -n MiProyecto.Worker -o src/MiProyecto.Worker -f net10.0

# Tests
dotnet new xunit -n MiProyecto.Domain.Tests -o tests/MiProyecto.Domain.Tests -f net10.0
dotnet new xunit -n MiProyecto.Application.Tests -o tests/MiProyecto.Application.Tests -f net10.0
dotnet new xunit -n MiProyecto.Integration.Tests -o tests/MiProyecto.Integration.Tests -f net10.0

# Añadir a la solución
dotnet sln add src/MiProyecto.Domain/MiProyecto.Domain.csproj
dotnet sln add src/MiProyecto.Application/MiProyecto.Application.csproj
dotnet sln add src/MiProyecto.Infrastructure/MiProyecto.Infrastructure.csproj
dotnet sln add src/MiProyecto.Api/MiProyecto.Api.csproj
dotnet sln add src/MiProyecto.Worker/MiProyecto.Worker.csproj
dotnet sln add tests/MiProyecto.Domain.Tests/MiProyecto.Domain.Tests.csproj
dotnet sln add tests/MiProyecto.Application.Tests/MiProyecto.Application.Tests.csproj
dotnet sln add tests/MiProyecto.Integration.Tests/MiProyecto.Integration.Tests.csproj

# Referencias entre proyectos (Hexagonal: dependencias hacia dentro)
# Application depende de Domain
dotnet add src/MiProyecto.Application reference src/MiProyecto.Domain

# Infrastructure depende de Application y Domain
dotnet add src/MiProyecto.Infrastructure reference src/MiProyecto.Application
dotnet add src/MiProyecto.Infrastructure reference src/MiProyecto.Domain

# Api depende de Application e Infrastructure (composición)
dotnet add src/MiProyecto.Api reference src/MiProyecto.Application
dotnet add src/MiProyecto.Api reference src/MiProyecto.Infrastructure

# Worker depende de Application e Infrastructure
dotnet add src/MiProyecto.Worker reference src/MiProyecto.Application
dotnet add src/MiProyecto.Worker reference src/MiProyecto.Infrastructure

# Tests
dotnet add tests/MiProyecto.Domain.Tests reference src/MiProyecto.Domain
dotnet add tests/MiProyecto.Application.Tests reference src/MiProyecto.Application
dotnet add tests/MiProyecto.Application.Tests reference src/MiProyecto.Domain
dotnet add tests/MiProyecto.Integration.Tests reference src/MiProyecto.Api
```

### 2.3 Paquetes NuGet necesarios

```bash
# Domain - SIN dependencias externas (puro)
# (no instalar nada aquí)

# Application
dotnet add src/MiProyecto.Application package MediatR

# Infrastructure
dotnet add src/MiProyecto.Infrastructure package MongoDB.Driver
dotnet add src/MiProyecto.Infrastructure package RabbitMQ.Client
dotnet add src/MiProyecto.Infrastructure package BCrypt.Net-Next
dotnet add src/MiProyecto.Infrastructure package Microsoft.Extensions.Options
dotnet add src/MiProyecto.Infrastructure package MailKit

# Api
dotnet add src/MiProyecto.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/MiProyecto.Api package Swashbuckle.AspNetCore
dotnet add src/MiProyecto.Api package MediatR

# Worker
dotnet add src/MiProyecto.Worker package RabbitMQ.Client

# Tests
dotnet add tests/MiProyecto.Domain.Tests package FluentAssertions
dotnet add tests/MiProyecto.Application.Tests package FluentAssertions
dotnet add tests/MiProyecto.Application.Tests package NSubstitute
dotnet add tests/MiProyecto.Integration.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/MiProyecto.Integration.Tests package Testcontainers.MongoDb
```

### 2.4 Código del Dominio

**`src/MiProyecto.Domain/Enums/UserRole.cs`**
```csharp
namespace MiProyecto.Domain.Enums;

public enum UserRole
{
    Guest = 0,
    User = 1,
    Worker = 2,
    Admin = 3
}
```

**`src/MiProyecto.Domain/ValueObjects/Email.cs`**
```csharp
using System.Text.RegularExpressions;
using MiProyecto.Domain.Exceptions;

namespace MiProyecto.Domain.ValueObjects;

public sealed partial record Email
{
    public string Value { get; }

    private Email(string value) => Value = value;

    public static Email Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email cannot be empty.");

        if (!EmailRegex().IsMatch(email))
            throw new DomainException($"Invalid email format: {email}");

        return new Email(email.ToLowerInvariant());
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public override string ToString() => Value;
}
```

**`src/MiProyecto.Domain/Entities/User.cs`**
```csharp
using MiProyecto.Domain.Enums;
using MiProyecto.Domain.ValueObjects;

namespace MiProyecto.Domain.Entities;

public class User
{
    public string Id { get; private set; } = null!;
    public string Username { get; private set; } = null!;
    public Email Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    private User() { } // Para MongoDB

    public static User Create(string username, Email email, string passwordHash, UserRole role)
    {
        return new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = username,
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    public static User CreateGuest()
    {
        return new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "guest",
            Email = Email.Create("guest@system.local"),
            PasswordHash = string.Empty,
            Role = UserRole.Guest,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    public void Deactivate() => IsActive = false;
    public void ChangeRole(UserRole newRole) => Role = newRole;
}
```

**`src/MiProyecto.Domain/Entities/EmailMessage.cs`**
```csharp
namespace MiProyecto.Domain.Entities;

public class EmailMessage
{
    public string Id { get; private set; } = null!;
    public string To { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public bool Sent { get; private set; }
    public DateTime? SentAt { get; private set; }

    public static EmailMessage Create(string to, string subject, string body)
    {
        return new EmailMessage
        {
            Id = Guid.NewGuid().ToString(),
            To = to,
            Subject = subject,
            Body = body,
            CreatedAt = DateTime.UtcNow,
            Sent = false
        };
    }

    public void MarkAsSent()
    {
        Sent = true;
        SentAt = DateTime.UtcNow;
    }
}
```

**`src/MiProyecto.Domain/Exceptions/DomainException.cs`**
```csharp
namespace MiProyecto.Domain.Exceptions;

public class DomainException(string message) : Exception(message);
```

**`src/MiProyecto.Domain/Ports/IUserRepository.cs`**
```csharp
using MiProyecto.Domain.Entities;

namespace MiProyecto.Domain.Ports;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task CreateAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
}
```

**`src/MiProyecto.Domain/Ports/IEmailQueue.cs`**
```csharp
using MiProyecto.Domain.Entities;

namespace MiProyecto.Domain.Ports;

public interface IEmailQueue
{
    Task EnqueueAsync(EmailMessage message, CancellationToken ct = default);
    Task<EmailMessage?> DequeueAsync(CancellationToken ct = default);
}
```

**`src/MiProyecto.Domain/Ports/IPasswordHasher.cs`**
```csharp
namespace MiProyecto.Domain.Ports;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```

### 2.5 Código de la Capa de Aplicación

**`src/MiProyecto.Application/Interfaces/IJwtService.cs`**
```csharp
using MiProyecto.Domain.Entities;

namespace MiProyecto.Application.Interfaces;

public interface IJwtService
{
    string GenerateToken(User user);
}
```

**`src/MiProyecto.Application/DTOs/LoginRequest.cs`**
```csharp
namespace MiProyecto.Application.DTOs;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string Role);
public record RegisterRequest(string Username, string Email, string Password);
public record UserDto(string Id, string Username, string Email, string Role, DateTime CreatedAt);
```

**`src/MiProyecto.Application/Commands/Login/LoginCommand.cs`**
```csharp
using MediatR;
using MiProyecto.Application.DTOs;

namespace MiProyecto.Application.Commands.Login;

public record LoginCommand(string Username, string Password) : IRequest<LoginResponse>;
```

**`src/MiProyecto.Application/Commands/Login/LoginHandler.cs`**
```csharp
using MediatR;
using MiProyecto.Application.DTOs;
using MiProyecto.Application.Interfaces;
using MiProyecto.Domain.Ports;

namespace MiProyecto.Application.Commands.Login;

public class LoginHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtService jwtService) : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await userRepository.GetByUsernameAsync(request.Username, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        var token = jwtService.GenerateToken(user);
        return new LoginResponse(token, user.Username, user.Role.ToString());
    }
}
```

**`src/MiProyecto.Application/Commands/RegisterUser/RegisterUserCommand.cs`**
```csharp
using MediatR;
using MiProyecto.Application.DTOs;

namespace MiProyecto.Application.Commands.RegisterUser;

public record RegisterUserCommand(string Username, string Email, string Password) : IRequest<UserDto>;
```

**`src/MiProyecto.Application/Commands/RegisterUser/RegisterUserHandler.cs`**
```csharp
using MediatR;
using MiProyecto.Application.DTOs;
using MiProyecto.Domain.Entities;
using MiProyecto.Domain.Enums;
using MiProyecto.Domain.Ports;
using MiProyecto.Domain.ValueObjects;

namespace MiProyecto.Application.Commands.RegisterUser;

public class RegisterUserHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher) : IRequestHandler<RegisterUserCommand, UserDto>
{
    public async Task<UserDto> Handle(RegisterUserCommand request, CancellationToken ct)
    {
        var existing = await userRepository.GetByUsernameAsync(request.Username, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Username '{request.Username}' already exists.");

        var email = Email.Create(request.Email);
        var hash = passwordHasher.Hash(request.Password);
        var user = User.Create(request.Username, email, hash, UserRole.User);

        await userRepository.CreateAsync(user, ct);

        return new UserDto(user.Id, user.Username, user.Email.Value, user.Role.ToString(), user.CreatedAt);
    }
}
```

**`src/MiProyecto.Application/Commands/SendEmail/EnqueueEmailCommand.cs`**
```csharp
using MediatR;

namespace MiProyecto.Application.Commands.SendEmail;

public record EnqueueEmailCommand(string To, string Subject, string Body) : IRequest;
```

**`src/MiProyecto.Application/Commands/SendEmail/EnqueueEmailHandler.cs`**
```csharp
using MediatR;
using MiProyecto.Domain.Entities;
using MiProyecto.Domain.Ports;

namespace MiProyecto.Application.Commands.SendEmail;

public class EnqueueEmailHandler(IEmailQueue emailQueue) : IRequestHandler<EnqueueEmailCommand>
{
    public async Task Handle(EnqueueEmailCommand request, CancellationToken ct)
    {
        var message = EmailMessage.Create(request.To, request.Subject, request.Body);
        await emailQueue.EnqueueAsync(message, ct);
    }
}
```

**`src/MiProyecto.Application/Queries/GetUserById/GetUserByIdQuery.cs`**
```csharp
using MediatR;
using MiProyecto.Application.DTOs;

namespace MiProyecto.Application.Queries.GetUserById;

public record GetUserByIdQuery(string Id) : IRequest<UserDto?>;

public class GetUserByIdHandler(
    MiProyecto.Domain.Ports.IUserRepository userRepository) : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await userRepository.GetByIdAsync(request.Id, ct);
        if (user is null) return null;
        return new UserDto(user.Id, user.Username, user.Email.Value, user.Role.ToString(), user.CreatedAt);
    }
}
```

**`src/MiProyecto.Application/DependencyInjection.cs`**
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace MiProyecto.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        return services;
    }
}
```

### 2.6 Código de Infraestructura

**`src/MiProyecto.Infrastructure/Persistence/MongoDbContext.cs`**
```csharp
using MongoDB.Driver;
using MiProyecto.Domain.Entities;

namespace MiProyecto.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<User> Users => _database.GetCollection<User>("users");
    public IMongoCollection<EmailMessage> EmailMessages => _database.GetCollection<EmailMessage>("email_messages");
}
```

**`src/MiProyecto.Infrastructure/Persistence/Repositories/UserRepository.cs`**
```csharp
using MongoDB.Driver;
using MiProyecto.Domain.Entities;
using MiProyecto.Domain.Ports;
using MiProyecto.Infrastructure.Persistence;

namespace MiProyecto.Infrastructure.Persistence.Repositories;

public class UserRepository(MongoDbContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await context.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        await context.Users.Find(u => u.Username == username).FirstOrDefaultAsync(ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await context.Users.Find(u => u.Email.Value == email).FirstOrDefaultAsync(ct);

    public async Task CreateAsync(User user, CancellationToken ct = default) =>
        await context.Users.InsertOneAsync(user, cancellationToken: ct);

    public async Task UpdateAsync(User user, CancellationToken ct = default) =>
        await context.Users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await context.Users.Find(_ => true).ToListAsync(ct);
}
```

**`src/MiProyecto.Infrastructure/Messaging/RabbitMqEmailQueue.cs`**
```csharp
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MiProyecto.Domain.Entities;
using MiProyecto.Domain.Ports;

namespace MiProyecto.Infrastructure.Messaging;

public class RabbitMqEmailQueue : IEmailQueue, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private const string QueueName = "email_queue";

    public RabbitMqEmailQueue(string connectionString)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();
        _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false)
            .GetAwaiter().GetResult();
    }

    public async Task EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);
        await _channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: QueueName,
            body: body,
            cancellationToken: ct);
    }

    public async Task<EmailMessage?> DequeueAsync(CancellationToken ct = default)
    {
        var result = await _channel.BasicGetAsync(QueueName, autoAck: true, ct);
        if (result is null) return null;
        var json = Encoding.UTF8.GetString(result.Body.ToArray());
        return JsonSerializer.Deserialize<EmailMessage>(json);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
```

**`src/MiProyecto.Infrastructure/Security/JwtService.cs`**
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MiProyecto.Application.Interfaces;
using MiProyecto.Domain.Entities;

namespace MiProyecto.Infrastructure.Security;

public class JwtSettings
{
    public string Secret { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;
    public int ExpirationMinutes { get; set; } = 60;
}

public class JwtService(IOptions<JwtSettings> settings) : IJwtService
{
    private readonly JwtSettings _settings = settings.Value;

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email.Value),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**`src/MiProyecto.Infrastructure/Security/BCryptPasswordHasher.cs`**
```csharp
using MiProyecto.Domain.Ports;

namespace MiProyecto.Infrastructure.Security;

public class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);
    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
```

**`src/MiProyecto.Infrastructure/DependencyInjection.cs`**
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiProyecto.Application.Interfaces;
using MiProyecto.Domain.Ports;
using MiProyecto.Infrastructure.Messaging;
using MiProyecto.Infrastructure.Persistence;
using MiProyecto.Infrastructure.Persistence.Repositories;
using MiProyecto.Infrastructure.Security;

namespace MiProyecto.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // MongoDB
        services.AddSingleton(_ => new MongoDbContext(
            configuration.GetConnectionString("MongoDB")!,
            configuration["MongoDB:DatabaseName"] ?? "miproyecto"));

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();

        // RabbitMQ
        services.AddSingleton<IEmailQueue>(_ =>
            new RabbitMqEmailQueue(configuration.GetConnectionString("RabbitMQ")!));

        // Security
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        return services;
    }
}
```

### 2.7 API (Minimal API)

**`src/MiProyecto.Api/Program.cs`**
```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MiProyecto.Api.Endpoints;
using MiProyecto.Application;
using MiProyecto.Infrastructure;
using MiProyecto.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Add layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
        };
    });
builder.Services.AddAuthorization();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapAuthEndpoints();
app.MapUserEndpoints();

app.Run();

// Para tests de integración
public partial class Program { }
```

**`src/MiProyecto.Api/Endpoints/AuthEndpoints.cs`**
```csharp
using MediatR;
using MiProyecto.Application.Commands.Login;
using MiProyecto.Application.Commands.RegisterUser;
using MiProyecto.Application.DTOs;

namespace MiProyecto.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (LoginRequest request, IMediator mediator) =>
        {
            try
            {
                var result = await mediator.Send(new LoginCommand(request.Username, request.Password));
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        })
        .WithName("Login")
        .AllowAnonymous()
        .Produces<LoginResponse>(200)
        .Produces(401);

        group.MapPost("/register", async (RegisterRequest request, IMediator mediator) =>
        {
            try
            {
                var result = await mediator.Send(
                    new RegisterUserCommand(request.Username, request.Email, request.Password));
                return Results.Created($"/api/users/{result.Id}", result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("Register")
        .AllowAnonymous()
        .Produces<UserDto>(201)
        .Produces(409);

        group.MapPost("/guest", async (IMediator mediator) =>
        {
            // Guest login sin contraseña
            return Results.Ok(new LoginResponse("guest-token", "guest", "Guest"));
        })
        .WithName("GuestLogin")
        .AllowAnonymous();
    }
}
```

**`src/MiProyecto.Api/Endpoints/UserEndpoints.cs`**
```csharp
using MediatR;
using MiProyecto.Application.DTOs;
using MiProyecto.Application.Queries.GetUserById;
using MiProyecto.Domain.Enums;

namespace MiProyecto.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/{id}", async (string id, IMediator mediator) =>
        {
            var user = await mediator.Send(new GetUserByIdQuery(id));
            return user is not null ? Results.Ok(user) : Results.NotFound();
        })
        .WithName("GetUser")
        .Produces<UserDto>(200)
        .Produces(404);

        group.MapGet("/admin/dashboard", () =>
        {
            return Results.Ok(new { message = "Admin dashboard" });
        })
        .RequireAuthorization(policy =>
            policy.RequireRole(UserRole.Admin.ToString()));
    }
}
```

**`src/MiProyecto.Api/appsettings.json`**
```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://admin:MongoPassword123!@server:30017",
    "RabbitMQ": "amqp://admin:RabbitPassword123!@server:30672"
  },
  "MongoDB": {
    "DatabaseName": "miproyecto"
  },
  "Jwt": {
    "Secret": "TuClaveSecretaSuperSeguraDeAlMenos32Caracteres!!",
    "Issuer": "MiProyecto",
    "Audience": "MiProyecto",
    "ExpirationMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### 2.8 Worker Service

**`src/MiProyecto.Worker/Program.cs`**
```csharp
using MiProyecto.Application;
using MiProyecto.Infrastructure;
using MiProyecto.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<EmailWorker>();

var host = builder.Build();
host.Run();
```

**`src/MiProyecto.Worker/EmailWorker.cs`**
```csharp
using MiProyecto.Domain.Ports;

namespace MiProyecto.Worker;

public class EmailWorker(
    ILogger<EmailWorker> logger,
    IEmailQueue emailQueue) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Email Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await emailQueue.DequeueAsync(stoppingToken);
                if (message is not null)
                {
                    logger.LogInformation("Sending email to {To}: {Subject}", message.To, message.Subject);
                    // Aquí iría la lógica de envío real con SmtpEmailService
                    message.MarkAsSent();
                    logger.LogInformation("Email sent successfully to {To}", message.To);
                }
                else
                {
                    await Task.Delay(5000, stoppingToken); // Polling interval
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing email queue");
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
```

---

## FASE 3: Dockerización

### 3.1 Dockerfile para la API

**`src/MiProyecto.Api/Dockerfile`**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar archivos de proyecto y restaurar
COPY ["src/MiProyecto.Api/MiProyecto.Api.csproj", "src/MiProyecto.Api/"]
COPY ["src/MiProyecto.Application/MiProyecto.Application.csproj", "src/MiProyecto.Application/"]
COPY ["src/MiProyecto.Domain/MiProyecto.Domain.csproj", "src/MiProyecto.Domain/"]
COPY ["src/MiProyecto.Infrastructure/MiProyecto.Infrastructure.csproj", "src/MiProyecto.Infrastructure/"]
RUN dotnet restore "src/MiProyecto.Api/MiProyecto.Api.csproj"

# Copiar todo y compilar
COPY . .
RUN dotnet publish "src/MiProyecto.Api/MiProyecto.Api.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MiProyecto.Api.dll"]
```

### 3.2 Dockerfile para el Worker

**`src/MiProyecto.Worker/Dockerfile`**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/MiProyecto.Worker/MiProyecto.Worker.csproj", "src/MiProyecto.Worker/"]
COPY ["src/MiProyecto.Application/MiProyecto.Application.csproj", "src/MiProyecto.Application/"]
COPY ["src/MiProyecto.Domain/MiProyecto.Domain.csproj", "src/MiProyecto.Domain/"]
COPY ["src/MiProyecto.Infrastructure/MiProyecto.Infrastructure.csproj", "src/MiProyecto.Infrastructure/"]
RUN dotnet restore "src/MiProyecto.Worker/MiProyecto.Worker.csproj"

COPY . .
RUN dotnet publish "src/MiProyecto.Worker/MiProyecto.Worker.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MiProyecto.Worker.dll"]
```

### 3.3 Dockerfile para el Frontend

**`frontend/Dockerfile`**
```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production

FROM nginx:alpine
COPY --from=build /app/www /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

**`frontend/nginx.conf`**
```nginx
server {
    listen 80;
    root /usr/share/nginx/html;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://api-service:8080/api/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

### 3.4 Docker Compose (desarrollo local)

**`docker-compose.yml`**
```yaml
services:
  mongodb:
    image: mongo:7
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: MongoPassword123!
    volumes:
      - mongo-data:/data/db

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: RabbitPassword123!

  api:
    build:
      context: .
      dockerfile: src/MiProyecto.Api/Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ConnectionStrings__MongoDB=mongodb://admin:MongoPassword123!@mongodb:27017
      - ConnectionStrings__RabbitMQ=amqp://admin:RabbitPassword123!@rabbitmq:5672
      - MongoDB__DatabaseName=miproyecto
    depends_on:
      - mongodb
      - rabbitmq

  worker:
    build:
      context: .
      dockerfile: src/MiProyecto.Worker/Dockerfile
    environment:
      - ConnectionStrings__MongoDB=mongodb://admin:MongoPassword123!@mongodb:27017
      - ConnectionStrings__RabbitMQ=amqp://admin:RabbitPassword123!@rabbitmq:5672
    depends_on:
      - mongodb
      - rabbitmq

volumes:
  mongo-data:
```

---

## FASE 4: CI/CD — Jenkinsfile

### 4.1 Jenkinsfile completo

**`Jenkinsfile`**
```groovy
pipeline {
    agent any

    environment {
        HARBOR_REGISTRY = 'server:8085'
        HARBOR_PROJECT  = 'mi-proyecto'
        SONAR_HOST      = 'http://server:9000'
        IMAGE_TAG       = "${env.BUILD_NUMBER}"
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Restore & Build') {
            steps {
                sh '''
                    dotnet restore
                    dotnet build --configuration Release --no-restore
                '''
            }
        }

        stage('Test') {
            steps {
                sh 'dotnet test --configuration Release --no-build --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"'
            }
            post {
                always {
                    // Publicar resultados de tests
                    mstest testResultsFile: '**/*.trx', keepLongStdio: true
                }
            }
        }

        stage('SonarQube Analysis') {
            steps {
                withCredentials([string(credentialsId: 'sonarqube-token', variable: 'SONAR_TOKEN')]) {
                    sh '''
                        dotnet tool install --global dotnet-sonarscanner || true
                        export PATH="$PATH:$HOME/.dotnet/tools"

                        dotnet sonarscanner begin \
                            /k:"mi-proyecto" \
                            /d:sonar.host.url="${SONAR_HOST}" \
                            /d:sonar.token="${SONAR_TOKEN}" \
                            /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"

                        dotnet build --configuration Release --no-restore

                        dotnet sonarscanner end /d:sonar.token="${SONAR_TOKEN}"
                    '''
                }
            }
        }

        stage('Quality Gate') {
            steps {
                timeout(time: 5, unit: 'MINUTES') {
                    waitForQualityGate abortPipeline: true
                }
            }
        }

        stage('Docker Build') {
            steps {
                sh """
                    docker build -t ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} \
                        -f src/MiProyecto.Api/Dockerfile .

                    docker build -t ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} \
                        -f src/MiProyecto.Worker/Dockerfile .

                    docker build -t ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} \
                        -f frontend/Dockerfile frontend/
                """
            }
        }

        stage('Trivy Security Scan') {
            steps {
                sh """
                    echo "=== Scanning API image ==="
                    trivy image --exit-code 1 --severity CRITICAL,HIGH \
                        --format table \
                        ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG}

                    echo "=== Scanning Worker image ==="
                    trivy image --exit-code 1 --severity CRITICAL,HIGH \
                        --format table \
                        ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG}

                    echo "=== Scanning Frontend image ==="
                    trivy image --exit-code 1 --severity CRITICAL,HIGH \
                        --format table \
                        ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG}
                """
            }
        }

        stage('Push to Harbor') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'harbor-credentials',
                    usernameVariable: 'HARBOR_USER',
                    passwordVariable: 'HARBOR_PASS')]) {
                    sh """
                        echo "${HARBOR_PASS}" | docker login ${HARBOR_REGISTRY} -u ${HARBOR_USER} --password-stdin

                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG}
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG}
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG}

                        # Tag latest
                        docker tag ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} \
                            ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:latest
                        docker tag ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} \
                            ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:latest
                        docker tag ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} \
                            ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:latest

                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:latest
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:latest
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:latest
                    """
                }
            }
        }

        stage('Deploy to K3s') {
            steps {
                withCredentials([file(credentialsId: 'kubeconfig', variable: 'KUBECONFIG')]) {
                    sh """
                        # Actualizar las imágenes en los deployments
                        kubectl set image deployment/api \
                            api=${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} \
                            -n mi-proyecto --kubeconfig=${KUBECONFIG}

                        kubectl set image deployment/worker \
                            worker=${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} \
                            -n mi-proyecto --kubeconfig=${KUBECONFIG}

                        kubectl set image deployment/frontend \
                            frontend=${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} \
                            -n mi-proyecto --kubeconfig=${KUBECONFIG}

                        # Esperar rollout
                        kubectl rollout status deployment/api -n mi-proyecto --timeout=120s --kubeconfig=${KUBECONFIG}
                        kubectl rollout status deployment/worker -n mi-proyecto --timeout=120s --kubeconfig=${KUBECONFIG}
                        kubectl rollout status deployment/frontend -n mi-proyecto --timeout=120s --kubeconfig=${KUBECONFIG}
                    """
                }
            }
        }
    }

    post {
        always {
            // Limpiar imágenes Docker locales
            sh """
                docker rmi ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} || true
                docker rmi ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} || true
                docker rmi ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} || true
            """
        }
        success {
            echo '✅ Pipeline completed successfully!'
        }
        failure {
            echo '❌ Pipeline failed!'
        }
    }
}
```

---

## FASE 5: Despliegue en K3s

### 5.1 Manifests de Kubernetes

**`k8s/namespace.yaml`**
```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: mi-proyecto
```

**`k8s/secrets.yaml`**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: app-secrets
  namespace: mi-proyecto
type: Opaque
stringData:
  mongodb-connection: "mongodb://admin:MongoPassword123!@mongodb.infrastructure.svc.cluster.local:27017"
  rabbitmq-connection: "amqp://admin:RabbitPassword123!@rabbitmq.infrastructure.svc.cluster.local:5672"
  jwt-secret: "TuClaveSecretaSuperSeguraDeAlMenos32Caracteres!!"
```

**`k8s/configmap.yaml`**
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: app-config
  namespace: mi-proyecto
data:
  MongoDB__DatabaseName: "miproyecto"
  Jwt__Issuer: "MiProyecto"
  Jwt__Audience: "MiProyecto"
  Jwt__ExpirationMinutes: "60"
```

**`k8s/api-deployment.yaml`**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api
  namespace: mi-proyecto
spec:
  replicas: 2
  selector:
    matchLabels:
      app: api
  template:
    metadata:
      labels:
        app: api
    spec:
      imagePullSecrets:
        - name: harbor-registry
      containers:
      - name: api
        image: server:8085/mi-proyecto/api:latest
        ports:
        - containerPort: 8080
        envFrom:
        - configMapRef:
            name: app-config
        env:
        - name: ConnectionStrings__MongoDB
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: mongodb-connection
        - name: ConnectionStrings__RabbitMQ
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: rabbitmq-connection
        - name: Jwt__Secret
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: jwt-secret
        readinessProbe:
          httpGet:
            path: /swagger/index.html
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
        livenessProbe:
          httpGet:
            path: /swagger/index.html
            port: 8080
          initialDelaySeconds: 15
          periodSeconds: 10
---
apiVersion: v1
kind: Service
metadata:
  name: api-service
  namespace: mi-proyecto
spec:
  type: NodePort
  selector:
    app: api
  ports:
  - port: 8080
    targetPort: 8080
    nodePort: 30080
```

**`k8s/worker-deployment.yaml`**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: worker
  namespace: mi-proyecto
spec:
  replicas: 1
  selector:
    matchLabels:
      app: worker
  template:
    metadata:
      labels:
        app: worker
    spec:
      imagePullSecrets:
        - name: harbor-registry
      containers:
      - name: worker
        image: server:8085/mi-proyecto/worker:latest
        envFrom:
        - configMapRef:
            name: app-config
        env:
        - name: ConnectionStrings__MongoDB
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: mongodb-connection
        - name: ConnectionStrings__RabbitMQ
          valueFrom:
            secretKeyRef:
              name: app-secrets
              key: rabbitmq-connection
```

**`k8s/frontend-deployment.yaml`**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: frontend
  namespace: mi-proyecto
spec:
  replicas: 2
  selector:
    matchLabels:
      app: frontend
  template:
    metadata:
      labels:
        app: frontend
    spec:
      imagePullSecrets:
        - name: harbor-registry
      containers:
      - name: frontend
        image: server:8085/mi-proyecto/frontend:latest
        ports:
        - containerPort: 80
---
apiVersion: v1
kind: Service
metadata:
  name: frontend-service
  namespace: mi-proyecto
spec:
  type: NodePort
  selector:
    app: frontend
  ports:
  - port: 80
    targetPort: 80
    nodePort: 30081
```

### 5.2 Desplegar por primera vez

```bash
# En Server:
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/secrets.yaml
kubectl apply -f k8s/configmap.yaml

# Crear secret para pull de Harbor (si es privado)
kubectl create secret docker-registry harbor-registry \
  --docker-server=server:8085 \
  --docker-username=admin \
  --docker-password=TU_PASSWORD_HARBOR \
  -n mi-proyecto

kubectl apply -f k8s/api-deployment.yaml
kubectl apply -f k8s/worker-deployment.yaml
kubectl apply -f k8s/frontend-deployment.yaml

# Verificar
kubectl get pods -n mi-proyecto
```

### 5.3 URLs finales

| Servicio | URL |
|---|---|
| API + Swagger | http://server:30080/swagger |
| Frontend | http://server:30081 |
| SonarQube | http://server:9000 |
| Grafana | http://server:31692 |
| Prometheus | http://server:31651 |
| Jenkins | http://server:8086 |
| GitLab | http://server:8081 |
| Harbor | https://server:8085 |
| RabbitMQ Management | http://server:31672 |
| MongoDB | server:30017 |

---

## FASE 6: Configurar GitLab + Webhook a Jenkins

### 6.1 Crear repositorio en GitLab

1. Accede a `http://server:8081`
2. **New Project → Create blank project**: `mi-proyecto`
3. En tu PC:

```bash
cd MiProyecto
git init
git remote add origin http://server:8081/TU_USUARIO/mi-proyecto.git
git add .
git commit -m "Initial commit: Hexagonal DDD .NET 10 project"
git push -u origin main
```

### 6.2 Webhook GitLab → Jenkins

1. En Jenkins: **New Item → Pipeline**: `mi-proyecto-pipeline`
   - Pipeline from SCM → Git
   - URL: `http://server:8081/TU_USUARIO/mi-proyecto.git`
   - Credentials: `gitlab-credentials`
   - Branch: `*/main`
   - Script path: `Jenkinsfile`
   - Activar: **Build Triggers → Build when a change is pushed to GitLab**
   - Copiar la URL del webhook que aparece

2. En GitLab: **Settings → Webhooks**
   - URL: `http://server:8086/project/mi-proyecto-pipeline` (la URL copiada)
   - Trigger: Push events
   - Desmarcar SSL verification (es local)

---

## Resumen del Pipeline Completo

```
Push a GitLab
    ↓ (webhook)
Jenkins Pipeline:
    1. ✅ Checkout código
    2. ✅ dotnet restore & build
    3. ✅ dotnet test (unit + integration)
    4. ✅ SonarQube analysis + Quality Gate
    5. ✅ Docker build (API + Worker + Frontend)
    6. ✅ Trivy security scan (CRITICAL + HIGH)
    7. ✅ Push images a Harbor
    8. ✅ Deploy a K3s (rolling update)
```

**Resultado:** cada push a main desencadena automáticamente build, análisis de calidad, escaneo de seguridad y despliegue.
