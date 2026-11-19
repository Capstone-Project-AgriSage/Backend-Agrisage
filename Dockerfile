# AgriSage API for staging or any container host (docs/deployment/STAGING.md).
# Configuration comes only from environment variables: appsettings.Local.json never enters the image (.dockerignore).
#
#   docker build -t agrisage-api .                                  the API: port 8080, liveness at GET /health
#   docker build --target migrations -t agrisage-migrations .       applies the EF migrations to a database, then exits
#
# Reference seed and the first Admin account run with the API image itself:
#   docker run --rm --env-file staging.env agrisage-api --seed --create-admin

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json dotnet-tools.json Directory.Build.props Directory.Packages.props ./
COPY src/AgriSage.Domain/AgriSage.Domain.csproj src/AgriSage.Domain/
COPY src/AgriSage.Application/AgriSage.Application.csproj src/AgriSage.Application/
COPY src/AgriSage.Infrastructure/AgriSage.Infrastructure.csproj src/AgriSage.Infrastructure/
COPY src/AgriSage.Api/AgriSage.Api.csproj src/AgriSage.Api/
RUN dotnet restore src/AgriSage.Api/AgriSage.Api.csproj
COPY src/ src/
RUN dotnet publish src/AgriSage.Api/AgriSage.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# A self-contained EF migrations bundle: `docker run --rm agrisage-migrations --connection "<connection string>"`.
FROM build AS migrations-build
RUN dotnet tool restore \
 && dotnet ef migrations bundle --project src/AgriSage.Infrastructure --startup-project src/AgriSage.Api \
      --configuration Release --target-runtime linux-x64 --self-contained --output /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrations
WORKDIR /migrations
COPY --from=migrations-build /out/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# Default target. TLS ends at the host's proxy: forwarded headers give the real scheme and client address.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
# Npgsql probes GSSAPI on connect; without this library it logs "Cannot load library libgssapi_krb5.so.2".
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AgriSage.Api.dll"]
