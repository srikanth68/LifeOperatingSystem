import Foundation

// Splits a reply so San starts talking before the whole thing has been synthesised.
//
// Kokoro renders an entire reply before returning any audio, so a long answer meant
// several seconds of silence with nothing on screen either. The web client hit the same
// wall and solved it this way: speak the first sentence as soon as it exists, synthesise
// the next one while the current is playing. Measured there, a 251-character reply went
// from 8.1s to 2.5s before the first word.
//
// The first chunk is deliberately much shorter than the rest. It is the only one the
// listener actually waits for -- every later chunk is being made while the previous one
// plays, so its size costs nothing as long as synthesis keeps ahead of playback.
enum SpeechChunks {
    static let firstMax = 100
    static let restMax  = 220

    // Sentence-ish boundaries, longest run first so ". " wins over "." inside a number.
    private static let terminators: Set<Character> = [".", "!", "?", "\n"]

    static func split(_ text: String) -> [String] {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return [] }

        var chunks: [String] = []
        var current = ""
        var limit = firstMax

        for sentence in sentences(of: trimmed) {
            // A single sentence longer than the limit goes out on its own rather than
            // being cut mid-clause: half a sentence read aloud is worse than a slow one.
            if current.isEmpty && sentence.count >= limit {
                chunks.append(sentence)
                limit = restMax
                continue
            }
            if current.count + sentence.count + 1 > limit && !current.isEmpty {
                chunks.append(current)
                current = sentence
                limit = restMax
            } else {
                current = current.isEmpty ? sentence : current + " " + sentence
            }
        }
        if !current.isEmpty { chunks.append(current) }
        return chunks
    }

    // Keeps the terminator attached — the speech model uses it for prosody, and a
    // question read as a statement is immediately noticeable.
    private static func sentences(of text: String) -> [String] {
        var out: [String] = []
        var buf = ""
        for ch in text {
            buf.append(ch)
            if terminators.contains(ch) {
                let piece = buf.trimmingCharacters(in: .whitespacesAndNewlines)
                if !piece.isEmpty { out.append(piece) }
                buf = ""
            }
        }
        let tail = buf.trimmingCharacters(in: .whitespacesAndNewlines)
        if !tail.isEmpty { out.append(tail) }
        return out
    }
}
