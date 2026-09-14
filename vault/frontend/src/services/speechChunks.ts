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

// A sentence over the first cap used to go out whole; on a measured call that was 229
// characters and 5.4s before the first word. The opening sentence is now cut at a clause
// boundary instead, matching the iPhone app.
const FIRST_MAX = 80;
const REST_MAX  = 220;   // ~7s to synthesise, ~17s to speak
const MIN_HEAD  = 25;    // never cut the opening so early it is a fragment, not a phrase

// Sentence-ish boundaries. Deliberately conservative: a split in the wrong place is
// audible as an odd pause, whereas a chunk that runs long only costs a little latency.
const BOUNDARY = /(?<=[.!?])\s+|\n+/;
const SPOKEN = /[\p{L}\p{N}]/u;
const CLAUSE_MARKS = ',;:—–';

// Cuts at the last clause mark (followed by a space) before `max`, else the last space.
// Null when there is no sensible cut, and the sentence then goes out whole.
function breakEarly(s: string, max: number): [string, string] | null {
  const last = Math.min(max, s.length) - 1;
  let cut = -1;
  for (let i = last; i >= MIN_HEAD; i--) {
    if (CLAUSE_MARKS.includes(s[i]) && s[i + 1] === ' ') { cut = i + 1; break; }
  }
  if (cut < 0) {
    for (let i = last; i >= MIN_HEAD; i--) {
      if (s[i] === ' ') { cut = i; break; }
    }
  }
  if (cut < 0) return null;
  const head = s.slice(0, cut).trim();
  const tail = s.slice(cut).trim();
  return head && tail ? [head, tail] : null;
}

export function splitForSpeech(text: string): string[] {
  const clean = text.trim();
  if (!clean) return [];

  // A "?!" or a stray "." after an emoji arrives as a piece of its own, and sent alone it
  // is a whole TTS request for no sound. Pieces with nothing to pronounce join the one
  // before them, or are dropped when there is none.
  const pieces: string[] = [];
  for (const p of clean.split(BOUNDARY).map(s => s.trim()).filter(Boolean)) {
    if (SPOKEN.test(p)) pieces.push(p);
    else if (pieces.length) pieces[pieces.length - 1] += ' ' + p;
  }

  // Only the opening chunk is worth cutting mid-sentence: it is the one being waited for.
  if (pieces.length && pieces[0].length > FIRST_MAX) {
    const cut = breakEarly(pieces[0], FIRST_MAX);
    if (cut) pieces.splice(0, 1, cut[0], cut[1]);
  }

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
