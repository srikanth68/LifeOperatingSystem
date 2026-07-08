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
| **Sutra** | Document vault — upload, categorize, expiry tracking | `5400` |
| **Karma** | Habits & goals tracker with Telegram check-in notifications | `5600` |
| **Frontend** | Unified React dashboard for all modules | `5173` |

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    Frontend (React)                      │
│               Unified dashboard · Port 5173               │
└──┬─────────┬──────────────┬──────────────┬────────────────┘
   │         │              │              │
┌──▼───┐ ┌───▼──┐       ┌───▼────┐    ┌───▼───┐
│Vault │ │Vitara│       │ Sutra  │    │ Karma │
│ :5000│ │:5100 │       │ :5400  │    │ :5600 │
└──┬───┘ └──┬───┘       └───┬────┘    └───┬───┘
   │        │               │             │
┌──▼───┐ ┌──▼───┐           │             │
│Worker│ │Worker│           │             │
│(sync)│ │(sync)│           │             │
└──┬───┘ └──┬───┘           │             │
   │        │               │             │
┌──▼───┐ ┌──▼───┐       ┌───▼────┐    ┌───▼───┐
│SQLite│ │SQLite│       │ SQLite │    │ SQLite│
└──────┘ └──────┘       └────────┘    └───────┘
```

Each module is fully independent — its own database, its own API, its own deployment.

### Design Principles

- **Module isolation** — each module can run, fail, and deploy independently
- **Config over code** — swap AI models, sync schedules, and API keys without touching source
- **Local-first** — everything runs on your machine, no cloud dependency
- **Graceful degradation** — if a module is down, the others keep working

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- PowerShell 7+ (Windows) or pwsh (macOS/Linux)

See **[SETUP.md](SETUP.md)** for the full step-by-step walkthrough (generating secrets, filling in each module's `.env`, API keys). Short version:

```bash
git clone https://github.com/srikanth68/LifeOperatingSystem.git
cd LifeOperatingSystem
```

Copy each module's `.env.template` to `.env`, generate a shared `JWT_SECRET` + login via `shared/auth-setup.csx` (see SETUP.md), then:

```bash
cd vault/frontend && npm install && cd ../..
.\maaya-start.ps1
```

One command spins up all APIs, workers, and the frontend:

```
 ╔══════════════════════════════════════╗
 ║          M A A Y A   O S             ║
 ╚══════════════════════════════════════╝

  Vault     http://localhost:5000  (API + Worker)
  Vitara    http://localhost:5100  (API + Worker)
  Sutra     http://localhost:5400  (API)
  Karma     http://localhost:5600  (API)
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

### Sutra — Document Vault
- Upload, categorize, and tag documents (identity, finance, insurance, contracts, etc.)
- Expiry tracking with a dedicated tracker view
- Full-text search across document metadata

### Karma — Habits & Goals
- Goal and habit tracking with progress views
- Daily check-in reminders via **Telegram**

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 8, Clean Architecture (Domain → Application → Infrastructure → API) |
| Database | SQLite per module (zero config, file-based) |
| Frontend | React 18, TypeScript, Vite, React Query |
| Workers | .NET BackgroundService with PeriodicTimer |
| Notifications | Telegram Bot API |
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

- [ ] Raspberry Pi deployment with external access
- [ ] WhatsApp notification support

---

## Built with AI Agents

This project was architected and built using **AI-assisted development** — directing Claude Code agents with architectural constraints, design decisions, and iterative refinement. The developer's role: vision, decisions, and quality control. The AI's role: velocity.

**[Read the full AI development methodology →](docs/AI-DEVELOPMENT.md)**

---

## License

Private project. All rights reserved.
