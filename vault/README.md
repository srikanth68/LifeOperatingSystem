# Vault - Personal Finance Aggregator

A self-hosted personal finance dashboard that aggregates bank accounts, credit cards, and transactions via Plaid API.

## Architecture

### Backend
- **.NET 8 Worker Service** — Nightly scheduler for automatic syncs
- **REST API** — Endpoints for dashboard and data queries
- **SQLite Database** — Local, self-hosted persistence
- **Plaid Integration** — Secure connection to financial institutions

### Frontend
- **React 18 + Vite** — Fast, responsive dashboard
- **TypeScript** — Type-safe development
- **Recharts** — Charts and analytics visualization
- **Responsive Design** — Mobile-friendly interface

## Project Structure

```
vault/
├── Vault.Worker/           # .NET Worker Service
│   ├── Models/             # Data models (Institution, Account, Transaction)
│   ├── Services/           # PlaidService, SyncService
│   ├── Jobs/               # ScheduledSyncWorker
│   ├── Data/               # VaultDbContext, EF Core setup
│   ├── Program.cs          # Startup configuration
│   └── appsettings.json    # Configuration
├── Vault.API/              # .NET REST API
│   ├── Controllers/        # API endpoints
│   ├── Models/             # DTOs
│   ├── Program.cs          # API startup
│   └── appsettings.json    # Configuration
├── frontend/               # React dashboard
│   ├── src/
│   │   ├── components/     # UI components
│   │   ├── pages/          # Page containers
│   │   ├── services/       # API clients
│   │   ├── styles/         # CSS styles
│   │   ├── types/          # TypeScript interfaces
│   │   ├── App.tsx         # Root component
│   │   └── main.tsx        # Entry point
│   ├── package.json        # Dependencies
│   ├── vite.config.ts      # Vite configuration
│   └── index.html          # HTML template
└── docs/                   # Documentation
```

## Setup & Installation

### Prerequisites
- .NET 8 SDK
- Node.js 18+
- Plaid account with API credentials

### Backend Setup

1. **Install .NET dependencies**
   ```bash
   cd Vault.Worker
   dotnet restore
   cd ../Vault.API
   dotnet restore
   ```

2. **Configure environment variables**
   ```bash
   # Create .env file or set in System Properties
   PLAID_CLIENT_ID=your_client_id
   PLAID_API_KEY=your_api_key
   PLAID_ENV=sandbox  # or 'production'
   SYNC_TIME=02:00    # Daily sync time (24-hour format)
   ```

3. **Run migrations**
   ```bash
   cd Vault.API
   dotnet ef database update
   cd ../Vault.Worker
   dotnet ef database update
   ```

4. **Start services**
   ```bash
   # Terminal 1: Worker Service
   cd Vault.Worker
   dotnet run

   # Terminal 2: API Server
   cd Vault.API
   dotnet run --launch-profile https
   ```

### Frontend Setup

1. **Install dependencies**
   ```bash
   cd frontend
   npm install
   ```

2. **Start development server**
   ```bash
   npm run dev
   ```

3. **Build for production**
   ```bash
   npm run build
   npm run preview
   ```

## API Endpoints

### Institutions
- `GET /api/institutions` — List all institutions
- `GET /api/institutions/{id}` — Get institution details

### Accounts
- `GET /api/accounts` — List all accounts
- `GET /api/accounts?institutionId={id}` — Filter by institution
- `GET /api/accounts/{id}` — Get account details

### Transactions
- `GET /api/transactions` — List transactions with filters
  - `?accountId={id}` — Filter by account
  - `?startDate=2024-01-01&endDate=2024-01-31` — Date range
  - `?category=Groceries` — Filter by category
- `GET /api/transactions/{id}` — Get transaction details

### Summary & Analytics
- `GET /api/summary` — Aggregated totals and categories
- `GET /api/summary/monthly-spending?months=12` — Monthly trend
- `GET /api/summary/category-breakdown?days=30` — Top categories

### Sync Management
- `POST /api/sync/trigger` — Manually trigger sync
- `GET /api/sync/status` — Recent sync history
- `GET /api/sync/status/latest` — Latest sync status

## Configuration

### Worker Service (`appsettings.json`)
```json
{
  "ConnectionStrings": {
    "VaultDb": "Data Source=vault.db"
  },
  "Vault": {
    "SyncTime": "02:00",
    "SyncIntervalDays": 1
  }
}
```

### Environment Variables
| Variable | Description | Example |
|----------|-------------|---------|
| `PLAID_CLIENT_ID` | Plaid API client ID | `client_id_abc123` |
| `PLAID_API_KEY` | Plaid API key | `secret_key_xyz789` |
| `PLAID_ENV` | Plaid environment | `sandbox` or `production` |
| `SYNC_TIME` | Daily sync time (24-hour) | `02:00` |

## Features

### Dashboard
- **Net Worth Snapshot** — Total balance across all accounts
- **By Institution** — Aggregate totals grouped by bank/card issuer
- **Top Categories** — Spending breakdown for the last month
- **Manual Sync** — Trigger data sync on demand

### Transactions
- **Full Transaction History** — Browse all transactions
- **Date Filtering** — View transactions by date range
- **Institution Filter** — Filter by specific account/bank
- **Category Breakdown** — See transactions by category

### Budget
- **Custom Budgets** — Set spending limits per category
- **Progress Tracking** — Visual indicators for over/under budget
- **Monthly Trends** — 12-month spending history
- **Alerts** — Notifications when budgets are exceeded

### Settings
- **API Configuration** — Customize backend URL
- **Data Privacy** — Clear all local data
- **About** — Version and environment info

## Database Schema

### Institutions
- `Id` (UUID) — Primary key
- `PlaidInstitutionId` (string, unique) — Plaid identifier
- `Name` (string) — Institution name
- `Logo` (URL)
- `Website` (URL)

### Accounts
- `Id` (UUID)
- `PlaidAccountId` (string, unique)
- `InstitutionId` (FK)
- `Name`, `Type`, `SubType`
- `Balance`, `AvailableBalance`
- `Currency`
- `IsActive` (boolean)

### Transactions
- `Id` (UUID)
- `PlaidTransactionId` (string, unique)
- `AccountId` (FK)
- `Amount`, `Currency`
- `TransactionDate`, `AuthorizedDate`
- `Description`, `MerchantName`
- `Category`, `SubCategory`
- `IsPending` (boolean)

### SyncMetadata
- `Id` (UUID)
- `LastSyncTime`
- `Status` (pending|in_progress|completed|failed)
- `TransactionsAdded`, `TransactionsUpdated`
- `ErrorMessage`
- `RetryCount`

## Error Handling

### Retry Logic
- **Automatic Retries** — Exponential backoff with jitter (3 attempts)
- **Sync Failures** — Logged to console and database
- **API Errors** — HTTP error responses with detailed messages

### Logging
- **File Logs** — `logs/vault-{date}.txt`
- **Console Output** — Real-time sync status
- **Structured Logging** — Serilog for diagnostic trails

## Security Considerations

1. **Plaid Integration**
   - API key stored in environment variables (never in code)
   - Credentials validated on startup
   - Secure HTTPS communication with Plaid

2. **Data Storage**
   - SQLite database on local machine
   - No cloud storage or external APIs (except Plaid)
   - Raw Plaid responses stored for audit trails

3. **Frontend**
   - Local storage for user preferences (budgets, settings)
   - No sensitive data cached in browser
   - CORS configured for local API only

## Development

### Building from Source
```bash
# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Publish
dotnet publish -c Release -o ./publish
```

### Frontend Development
```bash
# Dev server with hot reload
npm run dev

# Type checking
npm run tsc --noEmit

# Linting
npm run lint
```

## Troubleshooting

**Sync fails with "Plaid API key invalid"**
- Verify `PLAID_API_KEY` and `PLAID_CLIENT_ID` are set correctly
- Check Plaid account is active and credentials haven't expired

**Frontend can't reach API**
- Ensure API is running on `http://localhost:5000` (or configured in Settings)
- Check CORS is enabled in API (`Program.cs`)
- Verify network connectivity

**Database locked error**
- Check no other instances of the app are running
- Delete `vault.db-wal` and `vault.db-shm` files (SQLite temp files)

**Transactions not showing**
- Verify sync completed successfully (check `POST /api/sync/trigger` status)
- Confirm accounts are linked in Plaid
- Check date filters aren't excluding transaction range

## Roadmap

- [ ] OAuth2 authentication for multi-user deployments
- [ ] Recurring transaction detection
- [ ] Advanced budget forecasting
- [ ] Mobile app (React Native)
- [ ] Bill reminders and due dates
- [ ] Investment portfolio tracking
- [ ] CSV export for taxes

## License

MIT
