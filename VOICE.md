# Maaya Voice — San mic (STT) + speaker (TTS)

San can listen and speak. Both are **self-hosted, open-source, and local** —
no audio ever leaves Everest. San proxies to whatever OpenAI-compatible speech
services you point it at (same swappable pattern as `LLM_PROVIDER`).

```
Browser mic ──record──► San /api/voice/transcribe ──► Whisper (STT) ──► text ──► chat
San reply ──text──► San /api/voice/speak ──► Piper (TTS) ──► audio ──► browser plays
```

The mic 🎤 and speaker 🔊 buttons appear in San's chat **only when the matching
service is configured** — until then they're hidden and everything else works
normally.

## 1 & 2. Whisper (STT) and Piper (TTS) — now part of `docker compose up`

Both run as regular services in `docker-compose.yml` — `whisper` (image
`fedirz/faster-whisper-server:latest-cpu`) and `piper` (image
`ghcr.io/matatonic/openedai-speech-min:latest`). They come up automatically
with everything else; there's nothing to run by hand. San reaches them by
service name on the compose network (`http://whisper:8000`,
`http://piper:8000` — already set in `deploy/env/san.env`).

`piper`'s voice files live under the usual persistent-data mount, so they
survive rebuilds/redeploys just like every other module's data:
```
deploy/data/piper/voices/    # .onnx + .onnx.json voice model files
deploy/data/piper/config/    # voice_to_speaker.yaml
```

### Installing the `lessac` voice (the current default — female, high quality)

`deploy/env/san.env` is set to `PIPER_VOICE=lessac`. `en_US-lessac-high` is
Piper's highest-rated voice overall — by most rankings the clearest, most
natural-sounding voice in the whole catalog (American accent; no high-tier
British or Indian English voice exists in Piper's catalog as of this writing —
`en_GB-jenny_dioco` is the closest British option but only medium quality).

Pull it straight from Hugging Face on Everest — no file transfer needed:

```bash
cd $HOME/documents/maaya   # or wherever your maaya folder is
mkdir -p deploy/data/piper/voices deploy/data/piper/config
curl -L -o deploy/data/piper/voices/en_US-lessac-high.onnx \
  https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/high/en_US-lessac-high.onnx
curl -L -o deploy/data/piper/voices/en_US-lessac-high.onnx.json \
  https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/high/en_US-lessac-high.onnx.json
```

Then add a `lessac` entry to `deploy/data/piper/config/voice_to_speaker.yaml`
(the container auto-populates this file with its own defaults on first boot —
just add to it, don't replace it, or you'll lose the other default voices):

```bash
nano deploy/data/piper/config/voice_to_speaker.yaml
```
Add right after the first line (`tts-1:`):
```yaml
  lessac:
    model: voices/en_US-lessac-high.onnx
    speaker:
```
Save (`Ctrl+O`, `Enter`), exit (`Ctrl+X`), then reload just Piper's config:
```bash
docker compose restart piper
```

> **Want a different voice?** Same pattern — download the `.onnx` +
> `.onnx.json` from Hugging Face (rhasspy/piper-voices), give it its own entry
> in `voice_to_speaker.yaml`, and change `PIPER_VOICE` in `deploy/env/san.env`
> (then `docker compose up -d san` to apply — env-only, no rebuild). Other
> high-tier options already checked: `en_US-ryan-high` (deep male, the only
> high-tier male voice in the catalog).

## 3. Apply

```bash
cd ~/maaya
docker compose up -d --build     # whisper + piper are new containers — first
                                  # run pulls their images and starts them
```

Reload the dashboard → open San → the 🎤 and 🔊 buttons appear. Click 🎤, speak,
click ⏹ — it transcribes and sends. Toggle 🔊 to have San read replies aloud.
The 📞 call button in the iPhone app becomes available the same way, once
`/api/voice/status` reports both services ready.

**First-run note:** `whisper` downloads its model on first start (small,
a minute or so) — the mic button won't work until that finishes. Check with
`docker compose logs -f whisper`.

## Accessing the dashboard (mic needs HTTPS off-box)

The frontend is served two ways:
- `http://<host>:3000` — plain HTTP. Mic works only on the Mac itself
  (`http://localhost:3000`), since browsers allow mic on localhost without TLS.
- `https://<host>:3443` — HTTPS with a self-signed cert. **Use this from your
  laptop/phone over Meshnet** so the mic works. First visit shows a certificate
  warning (self-signed) — accept it once per device; the mic then works.

  e.g. `https://srp6888everest.nord:3443` or `https://100.126.41.41:3443`

All module APIs are reached same-origin through this one nginx (`/svc/<module>/…`),
so there's no mixed-content block and no CORS to configure.

## Notes

- **Both services optional & independent** — run one, both, or neither.
- **Swappable**: any OpenAI-compatible STT/TTS server works; change only the URL.
- **Nothing leaves the Mac**: mic audio → local nginx → local San → local
  Whisper/Piper → back. The LLM is local Gemma. No cloud in the voice path.
