# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — separate layer so restores are cached unless .csproj files change
COPY ["src/ThreatModelingAgent.Api/ThreatModelingAgent.Api.csproj", "src/ThreatModelingAgent.Api/"]
COPY ["src/ThreatModelingAgent.Domain/ThreatModelingAgent.Domain.csproj", "src/ThreatModelingAgent.Domain/"]
COPY ["src/ThreatModelingAgent.Infrastructure/ThreatModelingAgent.Infrastructure.csproj", "src/ThreatModelingAgent.Infrastructure/"]
RUN dotnet restore "src/ThreatModelingAgent.Api/ThreatModelingAgent.Api.csproj"

# Build
COPY . .
RUN dotnet build "src/ThreatModelingAgent.Api/ThreatModelingAgent.Api.csproj" \
    -c Release \
    --no-restore

# Publish
FROM build AS publish
RUN dotnet publish "src/ThreatModelingAgent.Api/ThreatModelingAgent.Api.csproj" \
    -c Release \
    --no-build \
    -o /app/publish \
    /p:UseAppHost=false

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root (CLAUDE.md §4.4 Least Privilege)
USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "ThreatModelingAgent.Api.dll"]
