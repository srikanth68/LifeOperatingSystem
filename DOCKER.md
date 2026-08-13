# Maaya OS — Docker Deployment (Everest)

The whole stack — 8 module APIs, 3 workers, the MCP agent gateway, and the React
frontend — runs from one `docker compose up`. Designed for the Everest Mac, where
llama.cpp (Gemma, `:8080`) and Sentinel (`:8787`) already run **on the host**.

```
Browser (any Meshnet device) ──► :3000 frontend (nginx)
                                 :5000-5700 module APIs
Any MCP agent (Claude, …) ────► :5900 MCP gateway ──► modules + NorthStar memory
containers ──► host.docker.internal ──► llama.cpp :8080, Sentinel :8787
```

## One-time migration (on the OLD machine)

1. Stop the running stack (Ctrl+C on `maaya-start.ps1`).
2. `.\deploy\collect-data.ps1` — copies live DBs + Sutra storage into `deploy/data/`.
3. Transfer the repo **including `deploy/env/` and `deploy/data/`** to Everest
   (both are gitignored — git alone won't carry them):
   `scp -r maaya srp6888@100.126.41.41:~/Documents/maaya` (or rsync/AirDrop).

## Run (on Everest)

```bash
cd ~/Documents/maaya             # the live checkout on Everest lives under Documents
docker compose up -d --build     # first build ≈ 5-10 min
docker compose ps                # everything should be "running"
```

Open **http://100.126.41.41:3000** from any Meshnet device.

## Point an external MCP agent at the gateway

For any MCP-capable agent (Claude, custom) that should drive Maaya's tools:
- MCP endpoint: `http://localhost:5900`  (from other machines: `http://100.126.41.41:5900`)
- Header: `X-API-Key: <MCP_API_KEY from deploy/env/mcp.env>`
- San's own chat needs none of this — its agent loop (`LLM_PROVIDER=llamacpp-agent`)
  calls the module APIs directly. Gemma stays at `http://localhost:8080/v1` (host-level).

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
- **"Everything is down" but every module answers on its own port** — the
  dashboard shows modules offline and login falls back to the password form
  (no PIN pad), yet `curl http://<host>:5000/api/auth/probe` returns 200. That
  is nginx, not the backends. It resolves each module hostname **once at worker
  startup**; `up -d --build` recreates the backend containers on new bridge IPs
  and leaves the frontend container running, so nginx keeps dialling dead
  addresses and answers 502 for everything. A module that happens to be
  reassigned its old IP keeps working, which makes it look random.

  ```bash
  docker compose restart frontend    # re-resolve; fixes it immediately
  ```

  `nginx-locations.conf` now sets `resolver 127.0.0.11 valid=10s` and puts each
  upstream in a variable, which forces per-request resolution and stops the
  problem recurring — but that config is baked into the image, so it only takes
  effect after `docker compose up -d --build frontend`.

  Quick way to tell this apart from a genuinely dead backend: compare the module
  through nginx against the module direct.

  ```bash
  curl -s -o /dev/null -w '%{http_code}\n' http://<host>:3000/svc/vault/api/auth/probe
  ```

  502 through nginx + 200 direct on `:5000` means stale DNS, not a down service.
- **Services "flip-flopping"** (TTS down, then a module down, then the LLM down,
  each recovering on retry) is almost always **host RAM pressure, not the
  services**. Everest has 16 GB; check `Activity Monitor → Memory` for swap use
  and `docker stats` for the containers. The usual hog is host-level
  `llama-server`: KV cache scales with `--ctx-size`, so a 64K context on a 4B
  model costs ~10 GB. Quantize the cache — `--cache-type-k q8_0
  --cache-type-v q8_0` halves it (~10.5 GB → ~6.5 GB) at negligible quality
  cost. With San's own agent loop (`LLM_PROVIDER=llamacpp-agent`) a 32K
  context is plenty — prompts run ~5-6K tokens — so
  `--ctx-size 32768 --cache-type-k q8_0 --cache-type-v q8_0` is the sweet spot
  (~3.4 GB total).
  Raising Docker Desktop's memory limit in this situation makes it *worse* —
  there's no headroom to give.
- **"No space left on device" while Docker Desktop shows a healthy Mac disk** —
  Docker's VM has its own virtual disk, sized independently of the host's free
  space. `docker system df`, then `docker system prune -a` (add `--volumes`
  only if you're sure; module data lives in `deploy/data/` bind mounts, not
  Docker volumes, so it survives either way).
- Logs: `docker compose logs -f san` (any service name from docker-compose.yml).
- Update after a git pull: `docker compose up -d --build`.

## Trusting the dashboard certificate (stop the HTTPS warning)

The dashboard's HTTPS cert (port 3443) is self-signed, so browsers show
"Your connection is not private" every visit. The cert now carries a proper
Subject Alternative Name for `100.126.41.41`, `srp6888everest.nord`, and
`localhost`, which makes it **trustable** — do this once per device and the
warning disappears for good.

**If your Meshnet IP is not `100.126.41.41`**, rebuild the frontend with your IP
first, then re-deploy:
```bash
docker compose build --build-arg TLS_SAN="IP:<your-ip>,DNS:srp6888everest.nord,DNS:localhost" frontend
docker compose up -d frontend
```

**Export the cert** from the running container (on Everest):
```bash
docker cp maaya-frontend-1:/etc/nginx/certs/maaya.crt ~/maaya.crt
```
Copy `~/maaya.crt` to each device you browse from.

**Windows (Chrome/Edge use the OS store):**
1. Double-click `maaya.crt` → **Install Certificate**
2. **Local Machine** → **Place all certificates in the following store** →
   **Trusted Root Certification Authorities**
3. Finish, then fully restart the browser.

**macOS:** double-click → add to **login** keychain → open it → **Trust** →
"When using this certificate: **Always Trust**".

**iPhone:** AirDrop/email `maaya.crt` to the phone → install the profile
(Settings → Profile Downloaded) → then **Settings → General → About → Certificate
Trust Settings** → enable full trust for it.

After trusting, visit `https://100.126.41.41:3443` (or the `.nord` name) — no
warning. Note it's bound to those exact hostnames, so browse by one of them, not
a different IP.

> Alternative with zero cert-install: **Tailscale** instead of NordVPN Meshnet.
> `tailscale serve` issues a real Let's Encrypt cert for your `*.ts.net` name,
> which every device already trusts — no warning, nothing to import. Only worth
> it if you're open to switching mesh VPNs.
