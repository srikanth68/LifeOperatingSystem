# Vault Setup Guide

## Quick Start (5 minutes)

### 1. Get Plaid Credentials
1. Sign up at https://plaid.com/
2. Create a new application
3. Copy your **Client ID** and **API Key** from the dashboard

### 2. Configure Environment
Create a `.env` file in the root directory:
```bash
PLAID_CLIENT_ID=your_client_id_here
PLAID_API_KEY=your_api_key_here
PLAID_ENV=sandbox
SYNC_TIME=02:00
```

Or set environment variables in Windows:
```powershell
$env:PLAID_CLIENT_ID = "your_client_id_here"
$env:PLAID_API_KEY = "your_api_key_here"
```

### 3. Start Backend

**Terminal 1 - Worker Service** (handles nightly syncs)
```bash
cd Vault.Worker
dotnet run
```

**Terminal 2 - API Server** (REST endpoints)
```bash
cd Vault.API
dotnet run
```

API will be available at: `http://localhost:5000`

### 4. Start Frontend

**Terminal 3**
```bash
cd frontend
npm install
npm run dev
```

Dashboard will open at: `http://localhost:3000`

## Testing the Integration

1. **Dashboard** → Click "Sync Now" to pull test data from Plaid
2. **Plaid Sandbox** → Link a test institution (use `user_good`/`pass_good`)
3. **Transactions** → View pulled transactions after sync
4. **Settings** → Configure API URL if needed

## Database

SQLite database automatically created at `vault.db` in project root.

### Reset Database
```bash
rm vault.db vault.db-wal vault.db-shm
cd Vault.API
dotnet ef database update
cd ../Vault.Worker
dotnet ef database update
```

## Plaid Sandbox vs Production

| Mode | Use Case | Account | Creds |
|------|----------|---------|-------|
| **Sandbox** | Development/testing | Free | `user_good` / `pass_good` |
| **Production** | Real accounts | Paid | Your real credentials |

Switch in `appsettings.json`:
```json
{
  "Plaid": {
    "Environment": "sandbox"  // or "production"
  }
}
```

## Troubleshooting

### API won't start
```
Error: Address already in use
```
→ Change port in `Vault.API/appsettings.json`:
```json
"Urls": "http://localhost:5001"
```

### Frontend can't reach API
→ Settings → Change API URL to match your API port

### Database errors
→ Delete `vault.db*` files and restart

### Plaid auth fails
→ Verify credentials in environment variables:
```bash
echo $env:PLAID_API_KEY  # PowerShell
env | grep PLAID_API_KEY # Bash
```

## Production Deployment

For self-hosted deployment:

1. **Build Release**
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. **Copy to Server**
   - Copy `publish/` directory
   - Copy `.env` with production Plaid keys

3. **Run Services**
   ```bash
   dotnet Vault.API.dll --urls "http://0.0.0.0:5000"
   dotnet Vault.Worker.dll
   ```

4. **Reverse Proxy** (nginx/Apache)
   - Route `/api/*` to API service
   - Serve React frontend from `/`

5. **Scheduler**
   - If on Windows, register Worker as service
   - If on Linux, use systemd or cron

## Next Steps

- Set spending budgets in Budget page
- Link your bank accounts via Plaid
- Enable automatic nightly syncs
- Export monthly reports
