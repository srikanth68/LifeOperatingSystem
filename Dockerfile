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
# ffmpeg: San's voice input. Browsers record Opus-in-WebM (or AAC-in-MP4 on Safari),
# and llama.cpp decodes audio with miniaudio, which reads only WAV/MP3/FLAC — so the
# recording has to be converted to 16 kHz mono WAV before Gemma can hear it. Costs
# ~80 MB in the image; buys speech-to-text with no speech service at all.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=build /out /app

# What is actually running in this image.
#
# The stack is deployed by copying a tarball to the box, not by pulling a branch,
# so nothing on the running machine knows which commit it came from -- and a
# tarball reusing an earlier filename has already caused a stale build to be
# deployed and debugged as though it were the new one. VERSION is written into the
# archive by scripts/release.sh and lands here, so the answer is one command away
# on any module, all of which share this image:
#
#     docker compose exec san cat /app/VERSION
#
# Copied last and tolerated missing: a hand-built image without it still works.
COPY VERSIO[N] /app/VERSION

# Default command is overridden per service in docker-compose.yml.
CMD ["dotnet", "/app/vault-api/Vault.API.dll"]
