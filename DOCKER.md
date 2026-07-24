# Maaya OS — Docker Deployment (Everest)

The whole stack — 8 module APIs, 3 workers, the MCP agent gateway, and the React
frontend — runs from one `docker compose up`. Designed for the Everest Mac, where
llama.cpp (Gemma, `:8080`) and Sentinel (`:8787`) already run **on the host**.

```
Browser (any Meshnet device) ──► :3000 frontend (nginx)
                                 :5000-5700 module APIs
Hermes / any MCP agent ────────► :5900 MCP gateway ──► modules + NorthStar memory
containers ──► host.docker.internal ──► llama.cpp :8080, Sentinel :8787
```

## One-time migration (on the OLD machine)

1. Stop the running stack (Ctrl+C on `maaya-start.ps1`).
2. `.\deploy\collect-data.ps1` — copies live DBs + Sutra storage into `deploy/data/`.
3. Transfer the repo **including `deploy/env/` and `deploy/data/`** to Everest
   (both are gitignored — git alone won't carry them):
   `scp -r maaya srp6888@100.126.41.41:~/maaya` (or rsync/AirDrop).

## Run (on Everest)

```bash
cd ~/maaya
docker compose up -d --build     # first build ≈ 5-10 min
docker compose ps                # everything should be "running"
```

Open **http://100.126.41.41:3000** from any Meshnet device.

## Point Hermes at the gateway

Hermes runs on Everest itself, so:
- MCP endpoint: `http://localhost:5900`  (from other machines: `http://100.126.41.41:5900`)
- Header: `X-API-Key: <MCP_API_KEY from deploy/env/mcp.env>`
- Gemma stays at `http://localhost:8080/v1` (host-level, unchanged).

## Config

- Per-module env: `deploy/env/<module>.env` (secrets — never committed).
- All data (SQLite + document storage): `deploy/data/<module>/` — back this
  folder up and you've backed up Maaya.
- Frontend origin allowed by APIs: `CORS_ORIGINS` in each env file
  (preset to `http://100.126.41.41:3000`; add LAN/hostname origins as needed).
- Sentinel/llama.cpp URLs use `host.docker.internal` (works on Docker Desktop
  for Mac out of the box).

## Notes & gotchas

- **Auth**: requests arriving through Docker's NAT look like private-range IPs,
  so the trusted-network PIN pad (not full login) appears for anyone who can
  reach the port. On a Meshnet-only host that's your own devices. If Everest
  also sits on a shared LAN, consider removing `AUTH_TRUSTED_NETWORKS` and the
  private-range trust in `shared/Maaya.Auth/TrustedNetwork.cs`.
- **Oura OAuth**: `OURA_REDIRECT_URI` is now `http://100.126.41.41:5100/api/oura/callback`
  — add that exact URI to the Oura developer-app settings or re-linking will fail.
  (Synced Oura data keeps flowing regardless; the token lives in vitara.db.)
- **Vault split-db**: there were historically two `vault.db` files (API/Worker).
  The migration takes the API one; in Docker both processes share `/data/vault/vault.db`.
- **Frontend build** uses `npx vite build` (skips `tsc`) because of a few known
  pre-existing type errors that don't affect the bundle.
- **Nexus + llama.cpp reachability**: Sentinel and Gemma must listen on
  `0.0.0.0` (or at least the Docker bridge) on the host — `127.0.0.1`-only
  binds are NOT reachable via `host.docker.internal` on macOS.
- Logs: `docker compose logs -f san` (any service name from docker-compose.yml).
- Update after a git pull: `docker compose up -d --build`.
