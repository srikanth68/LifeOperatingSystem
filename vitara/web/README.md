# Vitara web

Vitara (readings, labs, profiles) and Insight (what they mean) as one app of their own, outside
the Maaya shell: no sidebar, none of the other modules.

```
npm install
npm run dev        # http://localhost:3100  (needs Vitara.API on 5100, Vitara.Insight on 5110)
npm run typecheck
npm run build
```

## What it shares with Maaya, and what it does not

| | |
|---|---|
| **Backends** | Unchanged. `Vitara.API` (5100) and `Vitara.Insight` (5110) are the same services. |
| **Signing in** | Maaya's, untouched, for now: the vault server's `/api/auth` (PIN on a trusted network, username and password elsewhere) and the same tokens. Only the screen is new (`pages/SignIn.tsx`). |
| **Reachable from here** | `/svc/vault/api/auth/`, `/svc/vitara/`, `/svc/insight/`. Nothing else: the container's nginx answers 404 to every other `/svc/` path, so it cannot be used to reach the rest of Maaya. |
| **Timezone** | From the server (`GET /svc/vitara/api/clock`), not from a setting in the browser, so the page and the server can never disagree about what "today" is. |
| **Code** | Its own copy of the small services it needs (`auth`, `apiHost`, `moduleQuery`, `timezone`) and stylesheets. Nothing is imported from `vault/frontend`, which is what lets it leave the repo later. |

## Layout

```
src/App.tsx            the app: sign-in gate, product bar, Today | Insight switch
src/pages/SignIn.tsx   PIN pad or password form, whichever the server asks for
src/pages/VitaraModule.tsx, InsightModule.tsx   the two sections
src/components/health/ the shared kit: Shell, tiles, rings, charts, profile menu
src/services/          auth, profile (who this screen is about), timezone, api routing
```

Maaya's Vitara and Insight tabs now open a launcher that links here (`vault/frontend/src/components/ExternalApp.tsx`).

## Replacing Maaya's sign-in later

`App.tsx` talks to sign-in through `auth` and `pages/SignIn.tsx` only. Accounts of Vitara's own
(see `docs/VITARA-ACCOUNTS.md`) replace those two files and the `/svc/vault` route in `nginx.conf`;
nothing else in the app knows where tokens come from.
