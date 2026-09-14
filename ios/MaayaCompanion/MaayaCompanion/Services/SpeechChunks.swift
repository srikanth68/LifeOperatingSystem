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
    // Measured on a call: a 229-character opening sentence took 5.4s to synthesise,
    // because a sentence over the limit used to go out whole. The first chunk is now cut
    // at a clause boundary instead, so the wait before the first word stays short.
    static let firstMax = 80
    static let restMax  = 220

    // Never cut the first chunk so early that it is a fragment rather than a phrase.
    private static let minHead = 25

    private static let terminators: Set<Character> = [".", "!", "?", "\n"]
    private static let clauseMarks: Set<Character> = [",", ";", ":", "—", "–"]

    static func split(_ text: String) -> [String] {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return [] }

        var queue = mergeFragments(sentences(of: trimmed))
        var chunks: [String] = []
        var current = ""
        var limit = firstMax
        var i = 0

        while i < queue.count {
            let sentence = queue[i]
            i += 1

            if current.isEmpty && sentence.count > limit {
                // Only the opening chunk is worth cutting mid-sentence: it is the one being
                // waited for. Later ones go out whole, because half a sentence read aloud
                // sounds broken and they are synthesised while earlier audio plays.
                if chunks.isEmpty, let (head, tail) = breakEarly(sentence, within: firstMax) {
                    chunks.append(head)
                    queue.insert(tail, at: i)
                } else {
                    chunks.append(sentence)
                }
                limit = restMax
                continue
            }
            if !current.isEmpty && current.count + sentence.count + 1 > limit {
                chunks.append(current)
                current = ""
                limit = restMax
                i -= 1                     // weigh this sentence again against the new limit
                continue
            }
            current = current.isEmpty ? sentence : current + " " + sentence
        }
        if !current.isEmpty { chunks.append(current) }
        return chunks
    }

    // Cuts at the last clause mark (followed by a space) before `max`, else the last space.
    // Nil when there is no sensible cut, and the sentence then goes out whole.
    private static func breakEarly(_ s: String, within max: Int) -> (String, String)? {
        let chars = Array(s)
        let last = min(max, chars.count) - 1
        guard last >= minHead else { return nil }

        var cut: Int?
        for idx in stride(from: last, through: minHead, by: -1)
        where clauseMarks.contains(chars[idx]) && idx + 1 < chars.count && chars[idx + 1] == " " {
            cut = idx + 1
            break
        }
        if cut == nil {
            for idx in stride(from: last, through: minHead, by: -1) where chars[idx] == " " {
                cut = idx
                break
            }
        }
        guard let c = cut else { return nil }

        let head = String(chars[..<c]).trimmingCharacters(in: .whitespaces)
        let tail = String(chars[c...]).trimmingCharacters(in: .whitespaces)
        guard !head.isEmpty, !tail.isEmpty else { return nil }
        return (head, tail)
    }

    // A "?!" or a stray "." after an emoji arrives as a sentence of its own. Sent alone it
    // was a whole TTS request -- measured at 5.7s, queued behind the real speech -- for no
    // sound at all. Pieces with nothing to pronounce join the previous piece, or are
    // dropped when there is none.
    private static func mergeFragments(_ pieces: [String]) -> [String] {
        var out: [String] = []
        for p in pieces {
            if hasSpokenContent(p) {
                out.append(p)
            } else if !out.isEmpty {
                out[out.count - 1] += p
            }
        }
        return out
    }

    private static func hasSpokenContent(_ s: String) -> Bool {
        s.unicodeScalars.contains { CharacterSet.alphanumerics.contains($0) }
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
