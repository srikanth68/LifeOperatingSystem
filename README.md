<p align="center">
  <!-- Replace with your logo when ready -->
  <h1 align="center">Maaya OS</h1>
  <p align="center"><strong>Your Life, One Operating System.</strong></p>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" />
  <img src="https://img.shields.io/badge/React-18-61DAFB?style=flat-square&logo=react" />
  <img src="https://img.shields.io/badge/SQLite-Embedded-003B57?style=flat-square&logo=sqlite" />
  <img src="https://img.shields.io/badge/Telegram-Alerts-26A5E4?style=flat-square&logo=telegram" />
</p>

---

## The Problem

Your finances live in one app. Health data in another. Property docs in a filing cabinet. Reminders in three different places. You're the integration layer between a dozen disconnected tools — and nothing talks to anything else.

## The Solution

**Maaya OS** is a modular, self-hosted personal operating system that unifies your financial, health, property, and productivity data into one intelligent platform — with an AI assistant that sees across all of it.

No cloud vendor lock-in. No monthly subscriptions. Your data stays on your machine.

---

## Modules

| Module | What it does | Port |
|--------|-------------|------|
| **Vault** | Finances — bank sync via Plaid, transactions, budgets, spending trends | `5000` |
| **Vitara** | Health — Oura Ring integration, sleep, readiness, activity, bio-age scoring | `5100` |
| **Aasthi** | Property — real estate portfolio, contacts, documents, profit tracking | `5200` |
| **San** | AI Assistant — model-agnostic chat, reminders, alerts, cross-module activity feed | `5300` |
| **Frontend** | Unified React dashboard for all modules | `5173` |

> **Coming soon:** Nexus (social network & contacts), NorthStar (knowledge hub), Karma (habits & goals), Sutra (journal & reflection)

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                    Frontend (React)                  │
│              Unified dashboard · Port 5173           │
└──────┬──────────┬──────────┬──────────┬──────────────┘
       │          │          │          │
  ┌────▼───┐ ┌───▼────┐ ┌───▼────┐ ┌───▼───┐
  │ Vault  │ │ Vitara │ │ Aasthi │ │  San  │
  │  API   │ │  API   │ │  API   │ │  API  │
  │ :5000  │ │ :5100  │ │ :5200  │ │ :5300 │
  └────┬───┘ └───┬────┘ └───┬────┘ └───┬───┘
       │         │          │          │
  ┌────▼───┐ ┌───▼────┐    │     ┌────▼────┐
  │ Worker │ │ Worker │    │     │ Worker  │
  │ (sync) │ │ (sync) │    │     │ (alerts)│
  └────┬───┘ └───┬────┘    │     └────┬────┘
       │         │          │          │
  ┌────▼───┐ ┌───▼────┐ ┌──▼─────┐ ┌──▼─────┐
  │ SQLite │ │ SQLite │ │ SQLite │ │ SQLite │
  └────────┘ └────────┘ └────────┘ └────────┘
```

Each module is fully independent — its own database, its own API, its own deployment. San bridges them via live HTTP calls, not shared databases.

### Design Principles

- **Module isolation** — each module can run, fail, and deploy independently
- **Config over code** — swap AI models, sync schedules, and API keys without touching source
- **Local-first** — everything runs on your machine, no cloud dependency
- **Model-agnostic AI** — San's chat provider is an interface; swap Claude for GPT or Llama via env vars
- **Graceful degradation** — if a module is down, the others keep working

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- PowerShell 7+ (Windows) or pwsh (macOS/Linux)

### 1. Clone & configure

```bash
git clone https://github.com/srikanth68/LifeOperatingSystem.git
cd LifeOperatingSystem
```

Copy each module's `.env.template` to `.env` and fill in your keys:

```powershell
# Required for each module that needs secrets:
cp vault/.env.template vault/.env      # Plaid API keys
cp vitara/.env.template vitara/.env    # Oura OAuth credentials
cp san/.env.template san/.env          # Telegram bot + LLM API key
```

### 2. Install frontend dependencies

```bash
cd vault/frontend && npm install && cd ../..
```

### 3. Launch everything

```powershell
.\maaya-start.ps1
```

That's it. One command spins up all APIs, workers, and the frontend.

```
 ╔══════════════════════════════════════╗
 ║          M A A Y A   O S             ║
 ╚══════════════════════════════════════╝

  Vault     http://localhost:5000  (API + Worker)
  Vitara    http://localhost:5100  (API + Worker)
  Aasthi    http://localhost:5200  (API)
  San       http://localhost:5300  (API + Worker)
  Frontend  http://localhost:5173
```

---

## Module Deep Dives

### Vault — Financial Intelligence
- Automatic bank sync via **Plaid** (transactions, balances, accounts)
- Budget tracking with category-level breakdowns
- Spending trend analysis
- Worker runs daily at 1 AM for scheduled sync

### Vitara — Health & Biometrics
- **Oura Ring** OAuth integration (sleep, readiness, activity)
- Bio-age scoring algorithm
- Health protocol tracking
- Worker syncs every 6 hours

### Aasthi — Property Portfolio
- Real estate CRUD with profit/loss calculations
- Contact management per property (tenants, agents, contractors)
- Bulk document upload with on-disk storage
- Document download and management

### San — AI Assistant
- **Model-agnostic chat** — currently Claude, swappable to any LLM via config
- Chat context enriched with live data from all running modules
- Reminders with **Telegram** notifications
- Threshold-based spending alerts (auto-re-arming)
- Time-based alerts (goal deadlines, document expiry)
- Unified activity feed across all modules

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 8, Clean Architecture (Domain → Application → Infrastructure → API) |
| Database | SQLite per module (zero config, file-based) |
| Frontend | React 18, TypeScript, Vite, React Query |
| Workers | .NET BackgroundService with PeriodicTimer |
| Notifications | Telegram Bot API |
| AI | Anthropic Claude (swappable via `IChatProvider` interface) |
| Bank Sync | Plaid API |
| Health Data | Oura Ring API (OAuth 2.0) |

---

## Screenshots

<!-- Add your screenshots here -->
<!-- ![Dashboard](docs/screenshots/dashboard.png) -->
<!-- ![San Assistant](docs/screenshots/san-chat.png) -->
<!-- ![Vault Transactions](docs/screenshots/vault-transactions.png) -->

*Screenshots coming soon.*

---

## Roadmap

- [ ] **Nexus** — Social network, contact management, relationship tracking
- [ ] **NorthStar** — Obsidian-like knowledge hub for notes, goals, and planning
- [ ] **Karma** — Habit tracking, goal setting, streaks
- [ ] **Sutra** — Journaling, reflection, mood tracking
- [ ] Raspberry Pi deployment with external access
- [ ] WhatsApp notification support
- [ ] Additional LLM providers (OpenAI, Ollama, local models)

---

## License

Private project. All rights reserved.
