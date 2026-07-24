# Maaya OS — single backend image containing every module's published output.
# docker-compose runs one container per module from this image, selecting the
# right dll via `command:`. Data (SQLite dbs, Sutra storage) lives in a bind
# mount at /data — see docker-compose.yml.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

RUN dotnet publish vault/Vault.API/Vault.API.csproj           -c Release -o /out/vault-api      && \
    dotnet publish vault/Vault.Worker/Vault.Worker.csproj     -c Release -o /out/vault-worker   && \
    dotnet publish vitara/Vitara.API/Vitara.API.csproj        -c Release -o /out/vitara-api     && \
    dotnet publish vitara/Vitara.Worker/Vitara.Worker.csproj  -c Release -o /out/vitara-worker  && \
    dotnet publish aasthi/Aasthi.API/Aasthi.API.csproj        -c Release -o /out/aasthi-api     && \
    dotnet publish san/San.API/San.API.csproj                 -c Release -o /out/san-api        && \
    dotnet publish san/San.Worker/San.Worker.csproj           -c Release -o /out/san-worker     && \
    dotnet publish northstar/NorthStar.API/NorthStar.API.csproj -c Release -o /out/northstar-api && \
    dotnet publish sutra/Sutra.API/Sutra.API.csproj           -c Release -o /out/sutra-api      && \
    dotnet publish karma/Karma.API/Karma.API.csproj           -c Release -o /out/karma-api      && \
    dotnet publish nexus/Nexus.API/Nexus.API.csproj           -c Release -o /out/nexus-api      && \
    dotnet publish mcp/Maaya.Mcp/Maaya.Mcp.csproj             -c Release -o /out/mcp-gateway

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# System-wide timezone. Without this, Linux containers default to UTC, so
# DateTime.Now-based scheduling (e.g. Karma's habit reminders) fires at the
# wrong wall-clock hour. tzdata isn't guaranteed present in the base image,
# so install it explicitly rather than assume TZ silently works.
ARG TZ=America/New_York
ENV TZ=${TZ}
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
COPY --from=build /out /app
# Default command is overridden per service in docker-compose.yml.
CMD ["dotnet", "/app/vault-api/Vault.API.dll"]
