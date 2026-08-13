import { moduleApi } from './apiHost';
import { authHeaders } from './auth';

const SAN = moduleApi(5300);

// Continuous hands-free voice conversation with San ("call mode"), as opposed to
// the push-to-talk mic button in voice.ts. The loop is:
//
//   listening → (speech detected) capturing → transcribing → thinking → speaking → listening
//
// Voice activity detection is plain RMS energy off a Web Audio AnalyserNode —
// no model, no dependency. It calibrates against the room's own noise floor on
// start, so a quiet room and a noisy one both work without a magic constant.
//
// Every phase is surfaced through onState because the local stack is genuinely
// slow (transcription + the agent tool loop + TTS can total 20-30s per turn).
// A silent spinner would read as broken; naming the phase makes the wait legible.

export type CallState =
  | 'idle'         // not in a call
  | 'calibrating'  // sampling the room's noise floor
  | 'listening'    // waiting for the user to start talking
  | 'capturing'    // recording the user's utterance
  | 'transcribing' // the model is turning the audio into text
  | 'thinking'     // San is working
  | 'speaking';    // playing San's reply

export interface CallHandlers {
  onState: (state: CallState) => void;
  onUserText: (text: string) => void;
  onSanText: (text: string) => void;
  onError: (message: string) => void;
  // Sends a user utterance to San and resolves with the assistant's reply text.
  sendToSan: (text: string) => Promise<string>;
}

// ── Tuning ──────────────────────────────────────────────────────────────────
const TICK_MS = 50;              // VAD sampling interval
const CALIBRATE_MS = 700;        // ambient noise sampled before the first turn
const SPEECH_MULT = 2.5;         // RMS above noiseFloor * this = speech
const BARGE_MULT = 4.5;          // stricter while San is speaking (echo guard)
const MIN_FLOOR = 0.012;         // never trust a calibration quieter than this
const SILENCE_MS = 1200;         // trailing silence that ends an utterance
const MIN_SPEECH_MS = 350;       // ignore coughs/clicks shorter than this
const MAX_UTTERANCE_MS = 30_000; // hard cap so a stuck mic can't record forever
const BARGE_SUSTAIN_MS = 300;    // sustained speech needed to interrupt San

const sleep = (ms: number) => new Promise<void>(r => setTimeout(r, ms));

export class VoiceCall {
  private handlers: CallHandlers;
  private stream: MediaStream | null = null;
  private ctx: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  // Explicitly backed by ArrayBuffer (not ArrayBufferLike) — getByteTimeDomainData
  // only accepts the former.
  private buf: Uint8Array<ArrayBuffer> = new Uint8Array(new ArrayBuffer(0));
  private noiseFloor = MIN_FLOOR;
  private audio: HTMLAudioElement | null = null;
  private stopped = false;

  constructor(handlers: CallHandlers) {
    this.handlers = handlers;
  }

  async start(): Promise<void> {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error(
        'Microphone needs a secure connection. Use https://<host>:3443 instead of :3000 ' +
        '(the mic works on plain http only at http://localhost).'
      );
    }

    // Echo cancellation is not optional here: without it San's own voice comes
    // back through the mic, trips barge-in, and the call talks over itself.
    this.stream = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    });

    this.ctx = new AudioContext();
    if (this.ctx.state === 'suspended') await this.ctx.resume();
    this.analyser = this.ctx.createAnalyser();
    this.analyser.fftSize = 1024;
    this.buf = new Uint8Array(new ArrayBuffer(this.analyser.fftSize));
    this.ctx.createMediaStreamSource(this.stream).connect(this.analyser);

    await this.calibrate();
    void this.loop();
  }

  hangUp(): void {
    this.stopped = true;
    if (this.audio) { this.audio.pause(); this.audio = null; }
    this.stream?.getTracks().forEach(t => t.stop());
    void this.ctx?.close().catch(() => { /* already closed */ });
    this.stream = null;
    this.ctx = null;
    this.analyser = null;
    this.handlers.onState('idle');
  }

  // ── Audio level ───────────────────────────────────────────────────────────

  // Byte time-domain data rather than float: universally supported, including
  // older Safari, which is what the iPhone/iPad browse from.
  private rms(): number {
    if (!this.analyser) return 0;
    this.analyser.getByteTimeDomainData(this.buf);
    let sum = 0;
    for (let i = 0; i < this.buf.length; i++) {
      const v = (this.buf[i] - 128) / 128;
      sum += v * v;
    }
    return Math.sqrt(sum / this.buf.length);
  }

  private async calibrate(): Promise<void> {
    this.handlers.onState('calibrating');
    let peak = 0;
    for (let waited = 0; waited < CALIBRATE_MS; waited += TICK_MS) {
      peak = Math.max(peak, this.rms());
      await sleep(TICK_MS);
    }
    this.noiseFloor = Math.max(peak, MIN_FLOOR);
  }

  // ── Main loop ─────────────────────────────────────────────────────────────

  private async loop(): Promise<void> {
    try {
      await this.conversation();
    } catch (e) {
      // The loop runs detached (void this.loop()), so anything unexpected has to
      // be surfaced here or it becomes a silent unhandled rejection.
      if (!this.stopped) this.handlers.onError((e as Error).message);
    }
  }

  private async conversation(): Promise<void> {
    while (!this.stopped) {
      this.handlers.onState('listening');
      await this.waitForSpeech();
      if (this.stopped) return;

      this.handlers.onState('capturing');
      const blob = await this.captureUtterance();
      if (this.stopped) return;
      if (!blob) continue; // too short to be speech — back to listening

      this.handlers.onState('transcribing');
      let text: string;
      try {
        text = await this.transcribe(blob);
      } catch (e) {
        this.handlers.onError((e as Error).message);
        continue;
      }
      if (this.stopped) return;
      if (!text) continue; // silence or unintelligible — just keep listening

      this.handlers.onUserText(text);

      this.handlers.onState('thinking');
      let reply: string;
      try {
        reply = await this.handlers.sendToSan(text);
      } catch (e) {
        this.handlers.onError((e as Error).message);
        continue;
      }
      if (this.stopped) return;
      if (!reply) continue;

      this.handlers.onSanText(reply);
      this.handlers.onState('speaking');
      try {
        await this.speakWithBargeIn(reply);
      } catch (e) {
        this.handlers.onError((e as Error).message);
      }
    }
  }

  private async waitForSpeech(): Promise<void> {
    const threshold = this.noiseFloor * SPEECH_MULT;
    while (!this.stopped) {
      if (this.rms() > threshold) return;
      await sleep(TICK_MS);
    }
  }

  // Records until the user stops talking (SILENCE_MS of quiet) or the hard cap.
  // Returns null for utterances too short to be real speech.
  private async captureUtterance(): Promise<Blob | null> {
    if (!this.stream) return null; // hung up between listening and capturing
    const threshold = this.noiseFloor * SPEECH_MULT;
    const mime = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg']
      .find(t => MediaRecorder.isTypeSupported(t)) || '';
    const rec = new MediaRecorder(this.stream, mime ? { mimeType: mime } : undefined);
    const chunks: BlobPart[] = [];
    rec.ondataavailable = e => { if (e.data.size > 0) chunks.push(e.data); };

    const done = new Promise<void>(resolve => { rec.onstop = () => resolve(); });
    rec.start();

    let elapsed = 0;
    let silence = 0;
    let voiced = 0;
    while (!this.stopped && elapsed < MAX_UTTERANCE_MS) {
      if (this.rms() > threshold) { silence = 0; voiced += TICK_MS; }
      else { silence += TICK_MS; if (silence >= SILENCE_MS) break; }
      await sleep(TICK_MS);
      elapsed += TICK_MS;
    }

    // Hanging up stops the tracks, which can stop the recorder for us —
    // calling stop() again would throw InvalidStateError.
    if (rec.state !== 'inactive') { rec.stop(); await done; }

    if (this.stopped || voiced < MIN_SPEECH_MS) return null;
    return new Blob(chunks, { type: rec.mimeType || 'audio/webm' });
  }

  private async transcribe(blob: Blob): Promise<string> {
    const ext = blob.type.includes('mp4') ? 'mp4' : 'webm';
    const form = new FormData();
    form.append('audio', blob, `utterance.${ext}`);
    const res = await fetch(`${SAN}/api/voice/transcribe`, {
      method: 'POST', headers: authHeaders(), body: form,
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Transcription failed' }));
      throw new Error(err.error || 'Transcription failed');
    }
    const data = await res.json();
    return (data.text ?? '').trim();
  }

  // Plays San's reply, but keeps watching the mic: sustained speech over the
  // (higher) barge-in threshold cuts the playback short so the user can
  // interrupt instead of waiting out a long answer.
  private async speakWithBargeIn(text: string): Promise<void> {
    const res = await fetch(`${SAN}/api/voice/speak`, {
      method: 'POST',
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ text }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Speech failed' }));
      throw new Error(err.error || 'Speech failed');
    }

    const url = URL.createObjectURL(await res.blob());
    const audio = new Audio(url);
    this.audio = audio;
    const ended = new Promise<void>(resolve => {
      audio.onended = () => resolve();
      audio.onerror = () => resolve();
    });
    await audio.play();

    const threshold = this.noiseFloor * BARGE_MULT;
    let loud = 0;
    while (!this.stopped && !audio.ended) {
      if (this.rms() > threshold) {
        loud += TICK_MS;
        if (loud >= BARGE_SUSTAIN_MS) { audio.pause(); break; }
      } else {
        loud = 0;
      }
      await sleep(TICK_MS);
    }

    if (!audio.paused) await ended;
    URL.revokeObjectURL(url);
    if (this.audio === audio) this.audio = null;
  }
}

export const CALL_STATE_LABEL: Record<CallState, string> = {
  idle: 'Not in a call',
  calibrating: 'Getting a feel for the room…',
  listening: 'Listening',
  capturing: 'Hearing you…',
  transcribing: 'Making out what you said…',
  thinking: 'San is thinking…',
  speaking: 'San is speaking',
};
