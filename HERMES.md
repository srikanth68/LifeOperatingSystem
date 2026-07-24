# San → Hermes agent integration

Goal: type in San's chat → Hermes (a real agent harness) does the work → actions
actually happen (reminders/alerts/calendar created). All local — San, Hermes, the
Gemma model, and the Maaya MCP tools all run on Everest. Nothing goes to the cloud.

```
San chat UI ──► San.API (LLM_PROVIDER=hermes) ──► Hermes gateway (:8642, OpenAI-compatible)
                                                        │
                                              ┌─────────┴──────────┐
                                        Gemma via llama.cpp   Maaya.Mcp (:5900)
                                                              (reminder_create, …)
```

Why this and not "just prompt San harder": Hermes runs the **same** Gemma-4B model
San would — but inside a proper tool-calling loop. Reliability comes from the
harness, not the model. San's own approach (asking the model to hand-write a JSON
block in prose) is why it kept *saying* it created reminders without doing so.

## What's already built (Maaya side)

- **San** has a `hermes` provider (`San.Infrastructure/Llm/HermesChatProvider.cs`).
  When `LLM_PROVIDER=hermes`, San posts the chat to Hermes' OpenAI endpoint and,
  because Hermes handles tools itself, San skips its own action-block scaffolding.
- **Maaya.Mcp** (port 5900) now exposes write tools: `reminder_create`,
  `reminders_list`, `alert_create`, `calendar_event_create` (plus the existing
  read + memory tools). This is the toolset Hermes calls to actually do things.
  - Reachable at `http://localhost:5900` from the Everest host (published by compose).
  - Auth: send the `MCP_API_KEY` from `deploy/env/mcp.env` as a Bearer token or
    `X-API-Key` header. (Current value is in that file.)

## Setup on Everest (Hermes side — you do this)

1. **Enable the gateway.** In `~/.hermes/.env`:
   ```
   API_SERVER_ENABLED=true
   API_SERVER_KEY=<pick-a-key>
   ```
2. **Register Maaya.Mcp as an MCP server in Hermes**, so its toolset includes
   `reminder_create` etc. (Hermes' MCP-server config — check `hermes` docs /
   `~/.hermes/config.yaml` for the exact key). Point it at:
   ```
   url:     http://localhost:5900        (MCP streamable HTTP; try /sse if it wants an SSE URL)
   header:  Authorization: Bearer <MCP_API_KEY from deploy/env/mcp.env>
   ```
3. **Start the gateway:** `hermes gateway` → should log `listening on http://127.0.0.1:8642`.
4. **Sanity-check it answers** (from the Everest host):
   ```bash
   curl -s http://127.0.0.1:8642/v1/models -H "Authorization: Bearer <API_SERVER_KEY>"
   ```

## Point San at Hermes

In `deploy/env/san.env`, uncomment/set (a template block is already there):
```
LLM_PROVIDER=hermes
HERMES_BASE_URL=http://host.docker.internal:8642   # San is in Docker → host
HERMES_API_KEY=<the API_SERVER_KEY from step 1>
HERMES_MODEL=hermes-agent
```
Then apply (env-only, no rebuild): `docker compose up -d san`.

Now ask San: *"remind me to go to the mall at 4pm today"* → Hermes should call
`reminder_create` and the item should appear in San's Reminders tab.

## Two things to confirm with Hermes (honest unknowns)

The Maaya + San side is done and verified to compile. What I can't verify from here:

1. **Does Hermes let you register an external MCP server?** The design assumes yes
   (its own docs list it as an MCP client), but confirm the exact config. Ask Hermes:
   *"How do I add an external MCP server to your toolset? What's the config key/format?"*
2. **Do MCP tools work through `hermes gateway`, not just the CLI?** The gateway docs
   list its toolset as "terminal, file ops, web search, memory, skills" — MCP wasn't
   named explicitly. If MCP tools DON'T surface via the gateway, a fallback that still
   works: Hermes' **terminal** tool can `curl` San's API directly
   (`POST http://localhost:5300/api/reminders` — needs a Maaya JWT), or we drop back to
   San's deterministic intent layer. Ask Hermes:
   *"When running as the gateway API server, is my MCP-server toolset available, or only
   the built-in tools?"*

Get those two answers and we'll know if this works end-to-end or needs the fallback.

## Timezone note

Reminders store UTC. Tell Hermes to convert the user's wall-clock time to UTC before
calling `reminder_create` (`dueAt` must be ISO-8601 UTC like `2026-07-23T20:30:00Z`).
Hermes can read the user's timezone from the brain via the `facts_list` MCP tool
(key `timezone`).
