// Speaking San's reply a sentence at a time instead of all at once.
//
// Kokoro renders the WHOLE reply before returning a single byte, and synthesis time
// scales with length — measured on Everest at roughly 31 characters per second, so a
// 436-character answer is 12 seconds of silence before you hear anything. That silence
// sits on top of the model's own time, and it was the most irritating part of a voice
// call by some distance.
//
// Splitting on sentence boundaries lets the first clause start playing while the rest
// is still being made. Speech plays at roughly 12-13 characters per second and Kokoro
// synthesises at ~31, so once the first chunk is playing the pipeline stays comfortably
// ahead of the ear and the remaining joins are inaudible.
//
// The first chunk is deliberately much shorter than the rest: it is the only one whose
// synthesis time the listener actually waits through.

const FIRST_MAX = 100;   // ~3s to synthesise, ~8s to speak — enough cover for chunk 2
const REST_MAX  = 220;   // ~7s to synthesise, ~17s to speak

// Sentence-ish boundaries. Deliberately conservative: a split in the wrong place is
// audible as an odd pause, whereas a chunk that runs long only costs a little latency.
const BOUNDARY = /(?<=[.!?])\s+|\n+/;

export function splitForSpeech(text: string): string[] {
  const clean = text.trim();
  if (!clean) return [];

  const pieces = clean.split(BOUNDARY).map(s => s.trim()).filter(Boolean);
  const chunks: string[] = [];
  let buf = '';

  for (const piece of pieces) {
    const max = chunks.length === 0 ? FIRST_MAX : REST_MAX;
    if (!buf) { buf = piece; }
    else if (buf.length + 1 + piece.length <= max) { buf += ' ' + piece; }
    else { chunks.push(buf); buf = piece; }

    // A single sentence longer than the cap still has to go out on its own — better a
    // long chunk than a split mid-clause, which sounds broken.
    if (buf.length >= max) { chunks.push(buf); buf = ''; }
  }
  if (buf) chunks.push(buf);
  return chunks;
}

export interface SpeakChunksOptions {
  // Turns one chunk of text into a playable object URL.
  synth: (text: string) => Promise<string>;
  // Called with each <audio> as it starts, so callers can hold it for barge-in.
  onAudio?: (audio: HTMLAudioElement) => void;
  // Checked between and during chunks; true stops the rest.
  shouldStop?: () => boolean;
  // Awaited while a chunk plays. Lets call mode watch the mic for interruptions;
  // resolves early if the caller wants to cut San off. Returns true to stop.
  awaitPlayback?: (audio: HTMLAudioElement) => Promise<boolean>;
}

export async function speakChunks(text: string, opts: SpeakChunksOptions): Promise<void> {
  const chunks = splitForSpeech(text);
  if (chunks.length === 0) return;

  // Synthesis of chunk N+1 is kicked off before chunk N finishes playing, which is what
  // removes the gaps. Only ONE is ever in flight ahead — running them all at once would
  // queue behind each other on the TTS server and delay the one actually needed next.
  let next: Promise<string> | null = opts.synth(chunks[0]);

  for (let i = 0; i < chunks.length; i++) {
    if (opts.shouldStop?.()) break;

    let url: string;
    try {
      url = await next!;
    } catch {
      // A failed chunk ends the utterance rather than skipping it — a reply missing its
      // middle sentence is worse than one that stops short.
      break;
    }

    next = i + 1 < chunks.length ? opts.synth(chunks[i + 1]) : null;

    const audio = new Audio(url);
    opts.onAudio?.(audio);
    try {
      await audio.play();
      const stopped = opts.awaitPlayback
        ? await opts.awaitPlayback(audio)
        : await new Promise<boolean>(resolve => {
            audio.onended = () => resolve(false);
            audio.onerror = () => resolve(false);
          });
      if (stopped) { URL.revokeObjectURL(url); break; }
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  // Drop a prefetch nobody will play, so its object URL isn't leaked.
  next?.then(URL.revokeObjectURL).catch(() => {});
}
