# Maaya OS — Setup Guide

This branch (`handoff/noAIVersion`) includes four modules: **Vault** (finance), **Vitara** (health),
**Sutra** (documents), **Karma** (habits & goals), plus the unified frontend dashboard.

## 1. Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- PowerShell 7+ (Windows) or `pwsh` (macOS/Linux)

## 2. Clone

```bash
git clone -b handoff/noAIVersion https://github.com/srikanth68/LifeOperatingSystem.git
cd LifeOperatingSystem
```

## 3. Generate your auth secrets

Every module shares one `JWT_SECRET` and Vault holds the login credentials (username + bcrypt
password hash). Generate all three at once:

```bash
dotnet tool install -g dotnet-script   # one-time, if you don't already have it
dotnet script shared/auth-setup.csx
```

It will prompt for a password and print:

```
JWT_SECRET=<random-secret>
AUTH_USERNAME=admin
AUTH_PASSWORD_HASH=<bcrypt-hash>
```

Keep this terminal output handy — you'll paste these three values into **every** module's `.env`
in the next step (`JWT_SECRET` must be identical across all modules, or auth will fail).

## 4. Configure each module's `.env`

Copy each template and fill in the values:

```bash
cp vault/.env.template   vault/.env
cp vitara/.env.template  vitara/.env
cp sutra/.env.template   sutra/.env
cp karma/.env.template   karma/.env
```

| Module | Required | Where to get it |
|--------|----------|------------------|
| **vault/.env** | `JWT_SECRET`, `AUTH_USERNAME`, `AUTH_PASSWORD_HASH` (from step 3), `AUTH_PIN` (any 4+ digit PIN for local/trusted-network login) | — |
| | `PLAID_CLIENT_ID`, `PLAID_API_KEY` | [dashboard.plaid.com](https://dashboard.plaid.com) → Team Settings → Keys (free sandbox/development tier) |
| **vitara/.env** | `JWT_SECRET` (same value as Vault) | — |
| | `OURA_CLIENT_ID`, `OURA_CLIENT_SECRET` | [cloud.ouraring.com/oauth/applications](https://cloud.ouraring.com/oauth/applications) — only needed if you own an Oura Ring |
| | `MFP_USERNAME`, `MFP_PASSWORD` | Your MyFitnessPal login — optional, only needed for nutrition sync |
| | `USDA_API_KEY` | [fdc.nal.usda.gov/api-key-signup](https://fdc.nal.usda.gov/api-key-signup.html) — free, only needed for nutrition lookups |
| **sutra/.env** | `JWT_SECRET` (same value as Vault) | — |
| **karma/.env** | `JWT_SECRET` (same value as Vault) | — |
| | `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` | Optional — create a bot via [@BotFather](https://t.me/BotFather), message it once, then check `https://api.telegram.org/bot<token>/getUpdates` for your chat ID. Leave blank to skip habit-reminder notifications. |

Any module missing its optional third-party keys (Oura, MyFitnessPal, USDA, Telegram) will still
run — those features just won't be available until you add them later.

## 5. Install frontend dependencies

```bash
cd vault/frontend
npm install
cd ../..
```

## 6. Launch everything

```powershell
.\maaya-start.ps1
```

This spins up Vault (API + Worker), Vitara (API + Worker), Sutra, Karma, and the frontend in one
terminal with color-coded, interleaved logs. Press `Ctrl+C` to stop all of them.

| Module | URL |
|--------|-----|
| Vault | http://localhost:5000 |
| Vitara | http://localhost:5100 |
| Sutra | http://localhost:5400 |
| Karma | http://localhost:5600 |
| Frontend | http://localhost:5173 |

Open **http://localhost:5173** and log in with the username/password (or PIN, on localhost) from
step 3.

## 7. First run notes

- Each module creates its own fresh SQLite database on first launch (`vault/vault.db`,
  `vitara/vitara.db`, etc.) — nothing is pre-seeded, you're starting with a clean slate.
- Vault's API and background sync Worker share **one** database file (`vault/vault.db`) — don't
  run them from different working directories or you'll end up with split data.
- To link a bank account in Vault, you'll need a [Plaid](https://plaid.com) developer account
  (free sandbox tier works for testing with fake bank data; development tier for real accounts).
