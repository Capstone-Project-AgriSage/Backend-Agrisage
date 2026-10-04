# AgriSage API image. Build context = repository root:  docker build -t agrisage-api .
# Secrets are never baked into the image: pass them as environment variables when the container starts (see docker-compose.yml, .env.example).

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so the packages are cached until a package or project file changes.
# (global.json is not copied: it pins an exact SDK patch, the image brings the current .NET 10 SDK.)
COPY Directory.Build.props Directory.Packages.props ./
COPY src/AgriSage.Domain/AgriSage.Domain.csproj src/AgriSage.Domain/
COPY src/AgriSage.Application/AgriSage.Application.csproj src/AgriSage.Application/
COPY src/AgriSage.Infrastructure/AgriSage.Infrastructure.csproj src/AgriSage.Infrastructure/
COPY src/AgriSage.Api/AgriSage.Api.csproj src/AgriSage.Api/
RUN dotnet restore src/AgriSage.Api/AgriSage.Api.csproj

COPY src ./src
RUN dotnet publish src/AgriSage.Api/AgriSage.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Plain HTTP inside the container; the host maps a port to 8080. TLS belongs to whatever sits in front of it.
# Production is the safe default (no Swagger, no local origins); docker-compose.yml sets Development for local work.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# The base image ships a non-root user ("app"); never run the API as root.
USER $APP_UID
ENTRYPOINT ["dotnet", "AgriSage.Api.dll"]
