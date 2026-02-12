FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Project.Worker/Project.Worker.csproj", "src/Project.Worker/"]
COPY ["src/Project.Application/Project.Application.csproj", "src/Project.Application/"]
COPY ["src/Project.Domain/Project.Domain.csproj", "src/Project.Domain/"]
COPY ["src/Project.Infrastructure/Project.Infrastructure.csproj", "src/Project.Infrastructure/"]
RUN dotnet restore "src/Project.Worker/Project.Worker.csproj"
COPY . .
RUN dotnet publish "src/Project.Worker/Project.Worker.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Project.Worker.dll"]
