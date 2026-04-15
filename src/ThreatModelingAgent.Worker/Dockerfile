# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — separate layer so restores are cached unless .csproj files change
COPY ["src/ThreatModelingAgent.Worker/ThreatModelingAgent.Worker.csproj", "src/ThreatModelingAgent.Worker/"]
COPY ["src/ThreatModelingAgent.Domain/ThreatModelingAgent.Domain.csproj", "src/ThreatModelingAgent.Domain/"]
COPY ["src/ThreatModelingAgent.Infrastructure/ThreatModelingAgent.Infrastructure.csproj", "src/ThreatModelingAgent.Infrastructure/"]
RUN dotnet restore "src/ThreatModelingAgent.Worker/ThreatModelingAgent.Worker.csproj"

# Build
COPY . .
RUN dotnet build "src/ThreatModelingAgent.Worker/ThreatModelingAgent.Worker.csproj" \
    -c Release \
    --no-restore

# Publish
FROM build AS publish
RUN dotnet publish "src/ThreatModelingAgent.Worker/ThreatModelingAgent.Worker.csproj" \
    -c Release \
    --no-build \
    -o /app/publish \
    /p:UseAppHost=false

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root (CLAUDE.md §4.4 Least Privilege)
USER app

ENV DOTNET_ENVIRONMENT=Production

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "ThreatModelingAgent.Worker.dll"]
