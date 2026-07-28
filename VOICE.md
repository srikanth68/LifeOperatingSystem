# Maaya Voice — San mic (STT) + speaker (TTS) + hands-free calls

San can listen and speak. Both are **self-hosted, open-source, and local** —
no audio ever leaves Everest. San proxies to whatever OpenAI-compatible speech
services you point it at (same swappable pattern as `LLM_PROVIDER`).

```
Browser mic ──record──► San /api/voice/transcribe ──► Whisper (STT) ──► text ──► chat
San reply ──text──► San /api/voice/speak ──► Kokoro (TTS) ──► audio ──► browser plays
```

Three buttons appear in San's chat, each only when the service behind it is
configured (see `/api/voice/status`) — until then they're hidden and everything
else works normally:

| Button | Needs | What it does |
|---|---|---|
| 🎤 | STT | Push-to-talk. Click, speak, click ⏹ — transcribes and sends. |
| 🔊 | TTS | Toggle: San reads every reply aloud. |
| 📞 | STT + TTS | **Call mode** — hands-free back-and-forth, no clicking. |

## Call mode (hands-free)

Click 📞 and just talk. San answers when you pause, then listens again — no
button pressing between turns. Speak over San to cut them off mid-answer.
Esc or **Hang up** ends the call.

**Reachable from anywhere, not just San's tab.** A floating 📞 button sits at the
App root (`components/VoiceCallUI.tsx` + `services/voiceCallContext.tsx`,
mounted in `App.tsx` outside the per-module switch) — visible on every module,
and switching tabs mid-call doesn't hang up.

**Home-screen shortcut → straight into a call.** The dashboard is a minimal PWA
(`public/manifest.webmanifest`) with a "Call San" shortcut. On a phone: open the
dashboard once over HTTPS (`https://<host>:3443`), "Add to Home Screen", then
long-press the resulting icon → **Call San**. That opens `/?call=san`, which
`voiceCallContext.tsx` detects on load and auto-starts the call once voice
status confirms ready — one tap from the home screen to a live call, no
navigating the dashboard first.

The overlay names the phase it's in — *Listening → Hearing you → Making out what
you said → San is thinking → San is speaking* — because on a local stack a turn
genuinely can take 20-30s (Whisper cold-load + the agent tool loop + TTS). A
bare spinner would read as broken; naming the phase makes the wait legible.

**How it decides you're talking** (`vault/frontend/src/services/voiceSession.ts`):
plain RMS energy off a Web Audio `AnalyserNode` — no model, no dependency. On
start it samples the room for 700ms and sets its threshold relative to *that*
noise floor, so a quiet room and a noisy one both work without hand-tuning. A
pause of 1.2s ends your turn; interrupting San needs a higher threshold
sustained for 300ms, so San's own voice can't trigger it.

Mic capture forces `echoCancellation` — without it San's voice returns through
the mic, trips the interrupt, and the call talks over itself.

> **Tuning:** the constants are grouped under `── Tuning ──` at the top of
> `voiceSession.ts`. If it cuts you off mid-sentence raise `SILENCE_MS`; if a
> noisy room keeps it stuck in "Hearing you", raise `SPEECH_MULT`.

## Whisper (STT) and Kokoro (TTS) — part of `docker compose up`

Both are regular services in `docker-compose.yml`:

| Service | Image | San reaches it at |
|---|---|---|
| `whisper` | `fedirz/faster-whisper-server:latest-cpu` | `http://whisper:8000` |
| `kokoro` | `ghcr.io/remsky/kokoro-fastapi-cpu:latest` | `http://kokoro:8880` |

Both URLs are already set in `deploy/env/san.env` (`WHISPER_SERVICE_URL`,
`TTS_SERVICE_URL`). They come up with everything else — nothing to run by hand.

**Voice**: `TTS_VOICE=bf_emma` (British female, Kokoro's top tier). Kokoro ships
its voices in the image — unlike Piper there are no model files to download.
To change it, set `TTS_VOICE` in `deploy/env/san.env` and
`docker compose up -d san` (env-only, no rebuild).

**Cold start**: both services load their model on first use after idling and
unload it again after ~5 minutes. The first request after a quiet spell can
take 5-10s or occasionally fail outright — retry once before assuming the
service is down.

### Falling back to Piper

`piper` (`ghcr.io/matatonic/openedai-speech-min:latest`) is still in
`docker-compose.yml` as a fallback. To switch, set in `deploy/env/san.env`:

```
TTS_SERVICE_URL=http://piper:8000
TTS_VOICE=lessac
```

Piper needs its voice files on disk (Kokoro doesn't), under the persistent
mount so they survive rebuilds:

```bash
cd ~/Documents/maaya
mkdir -p deploy/data/piper/voices deploy/data/piper/config
curl -L -o deploy/data/piper/voices/en_US-lessac-high.onnx \
  https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/high/en_US-lessac-high.onnx
curl -L -o deploy/data/piper/voices/en_US-lessac-high.onnx.json \
  https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/high/en_US-lessac-high.onnx.json
```

Then add a `lessac` entry to `deploy/data/piper/config/voice_to_speaker.yaml`
(the container writes its own defaults there on first boot — add to that file,
don't replace it, or you'll lose the other voices). Right after `tts-1:`:

```yaml
  lessac:
    model: voices/en_US-lessac-high.onnx
    speaker:
```

Then `docker compose restart piper`.

## Apply

```bash
cd ~/Documents/maaya
docker compose up -d --build
```

Reload the dashboard → open San → 🎤, 🔊, and 📞 appear.

## Accessing the dashboard (mic needs HTTPS off-box)

The frontend is served two ways:
- `http://<host>:3000` — plain HTTP. Mic works **only on the Mac itself**
  (`http://localhost:3000`), since browsers allow mic on localhost without TLS.
- `https://<host>:3443` — HTTPS with a self-signed cert. **Use this from your
  laptop/phone over Meshnet** so the mic works, e.g.
  `https://srp6888everest.nord:3443` or `https://100.126.41.41:3443`.
  Trust the cert once per device to drop the warning — see DOCKER.md →
  "Trusting the dashboard certificate".

All module APIs are reached same-origin through this one nginx
(`/svc/<module>/…`), so there's no mixed-content block and no CORS to configure.

The San proxy carries a 300s read timeout (`nginx-locations.conf`) — long
enough for a multi-step agent turn. Without it nginx cuts the connection at 60s and
the chat dies mid-"thinking".

## Notes

- **Every piece is optional & independent** — run STT, TTS, both, or neither.
  Call mode simply doesn't appear unless both are up.
- **Swappable**: any OpenAI-compatible STT/TTS server works; change only the URL.
- **Nothing leaves the Mac**: mic audio → local nginx → local San → local
  Whisper/Kokoro → back. The LLM is local Gemma via llama.cpp. No cloud in the
  voice path.
