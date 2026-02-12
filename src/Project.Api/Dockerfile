FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Project.Api/Project.Api.csproj", "src/Project.Api/"]
COPY ["src/Project.Application/Project.Application.csproj", "src/Project.Application/"]
COPY ["src/Project.Domain/Project.Domain.csproj", "src/Project.Domain/"]
COPY ["src/Project.Infrastructure/Project.Infrastructure.csproj", "src/Project.Infrastructure/"]
RUN dotnet restore "src/Project.Api/Project.Api.csproj"
COPY . .
RUN dotnet publish "src/Project.Api/Project.Api.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Project.Api.dll"]
