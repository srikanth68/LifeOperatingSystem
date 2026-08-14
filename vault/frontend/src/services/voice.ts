import { moduleApi } from './apiHost';
import { authHeaders } from './auth';
import { speakChunks } from './speechChunks';

const SAN = moduleApi(5300);

// sttEngine ("gemma") is informational — the quickest confirmation that the server
// is running the build you think it is. Optional: older San builds don't send it.
export interface VoiceStatus { sttReady: boolean; ttsReady: boolean; sttEngine?: string }

export async function getVoiceStatus(): Promise<VoiceStatus> {
  try {
    const res = await fetch(`${SAN}/api/voice/status`, { headers: authHeaders() });
    if (res.ok) return await res.json();
  } catch { /* San unreachable */ }
  return { sttReady: false, ttsReady: false };
}

// Records mic audio until stop() is called, then returns the transcribed text
// from San's transcribe endpoint. Returns a controller so the caller can stop it.
export interface Recorder { stop: () => Promise<string> }

export async function startRecording(): Promise<Recorder> {
  // navigator.mediaDevices is only exposed by browsers in a secure context
  // (https://, or http://localhost) — over plain HTTP on a LAN/mesh IP it's
  // simply undefined, which is exactly what shows up as this cryptic error.
  if (!navigator.mediaDevices?.getUserMedia) {
    throw new Error(
      'Microphone needs a secure connection. Use https://<host>:3443 instead of :3000 ' +
      '(the mic works on plain http only at http://localhost).'
    );
  }
  const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  // Pick a mime type the browser actually supports (Safari vs Chrome differ).
  const mime = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg']
    .find(t => MediaRecorder.isTypeSupported(t)) || '';
  const rec = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined);
  const chunks: BlobPart[] = [];
  rec.ondataavailable = e => { if (e.data.size > 0) chunks.push(e.data); };
  rec.start();

  return {
    stop: () => new Promise<string>((resolve, reject) => {
      rec.onstop = async () => {
        stream.getTracks().forEach(t => t.stop()); // release the mic
        try {
          const blob = new Blob(chunks, { type: rec.mimeType || 'audio/webm' });
          const form = new FormData();
          const ext = (rec.mimeType || 'audio/webm').includes('mp4') ? 'mp4' : 'webm';
          form.append('audio', blob, `recording.${ext}`);
          const res = await fetch(`${SAN}/api/voice/transcribe`, {
            method: 'POST', headers: authHeaders(), body: form,
          });
          if (!res.ok) {
            const err = await res.json().catch(() => ({ error: 'Transcription failed' }));
            reject(new Error(err.error || 'Transcription failed'));
            return;
          }
          const data = await res.json();
          resolve((data.text ?? '').trim());
        } catch (e) { reject(e); }
      };
      rec.stop();
    }),
  };
}

let currentAudio: HTMLAudioElement | null = null;

// Bumped by stopSpeaking(). A chunked utterance spans several fetches and plays, so
// "stop" cannot just pause the current element — the loop has to know its turn is over.
// A counter rather than a boolean so a NEW utterance starting immediately after a stop
// isn't cancelled by the old one's cleanup.
let speakGeneration = 0;

// Fetches one chunk of speech and returns a playable object URL.
async function synthChunk(text: string): Promise<string> {
  const res = await fetch(`${SAN}/api/voice/speak`, {
    method: 'POST',
    headers: { ...authHeaders(), 'Content-Type': 'application/json' },
    body: JSON.stringify({ text }),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: 'Speech failed' }));
    throw new Error(err.error || 'Speech failed');
  }
  return URL.createObjectURL(await res.blob());
}

// Speaks a reply sentence by sentence so the first words start while the rest is still
// being synthesised — see speechChunks. Kokoro renders a whole reply before returning
// anything, which meant up to 12s of silence on a long answer.
export async function speak(text: string): Promise<void> {
  stopSpeaking();
  const mine = ++speakGeneration;
  await speakChunks(text, {
    synth: synthChunk,
    onAudio: a => { currentAudio = a; },
    // stopSpeaking() bumps the generation, which abandons the rest of this utterance
    // without touching a newer one that may already have started.
    shouldStop: () => mine !== speakGeneration,
  });
}

export function stopSpeaking(): void {
  speakGeneration++;
  if (currentAudio) { currentAudio.pause(); currentAudio = null; }
}
