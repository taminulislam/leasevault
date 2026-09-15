# ---- Build stage -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so layer caching survives source changes.
COPY LeaseVault.sln ./
COPY src/LeaseVault.Core/LeaseVault.Core.csproj src/LeaseVault.Core/
COPY src/LeaseVault.Infrastructure/LeaseVault.Infrastructure.csproj src/LeaseVault.Infrastructure/
COPY src/LeaseVault.Web/LeaseVault.Web.csproj src/LeaseVault.Web/
COPY tests/LeaseVault.Tests/LeaseVault.Tests.csproj tests/LeaseVault.Tests/
RUN dotnet restore LeaseVault.sln

COPY . .
RUN dotnet build LeaseVault.sln -c Release --no-restore

# ---- Test stage (fails the image build when a test fails) ---------------------------------------
FROM build AS test
RUN dotnet test tests/LeaseVault.Tests/LeaseVault.Tests.csproj -c Release --no-build

# ---- Publish stage -----------------------------------------------------------------------------
FROM build AS publish
RUN dotnet publish src/LeaseVault.Web/LeaseVault.Web.csproj -c Release --no-build -o /app/publish

# ---- Runtime stage -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

# Local SQLite database and document store live here when no Azure services are configured.
RUN mkdir -p /app/App_Data && chown -R app:app /app
USER app

COPY --from=publish /app/publish .
EXPOSE 8080
# Liveness is probed externally on /health (App Service health check, container orchestrator);
# the slim aspnet image ships without curl/wget, so no in-image HEALTHCHECK is declared.
ENTRYPOINT ["dotnet", "LeaseVault.Web.dll"]
