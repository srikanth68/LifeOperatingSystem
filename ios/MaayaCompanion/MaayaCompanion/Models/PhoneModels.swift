import Foundation

// Models for the screens added so the phone can stand in for the laptop: today's
// calendar on Now, the memory review queue on Memory, and the server check in Settings.

// ── San — GET 5300/api/calendar/now-next?hours=N ──
// Times arrive as UTC ("…Z"), so they decode straight to Date. The server also sends
// the times pre-formatted in the owner's zone; the phone formats its own instead, since
// it is already in that zone whenever the owner is.
struct CalendarEventItem: Codable, Identifiable {
    let id: String
    let title: String
    let location: String?
    let startTime: Date
    let endTime: Date
    let allDay: Bool
}

struct NowNextResult: Codable {
    let current: CalendarEventItem?
    let upcoming: [CalendarEventItem]
}

// ── NorthStar — GET 5500/api/memory/review ──
// What San and the email reader have proposed and nobody has confirmed. A proposed
// memory is recalled marked "unconfirmed", so confirming one makes San state it as fact
// and retracting one stops it being recalled at all -- without deleting the record of
// the mistake, which is what Delete on the memory list does.
struct MemoryReviewResult: Codable {
    let count: Int
    let memories: [ReviewMemoryItem]
}

struct ReviewMemoryItem: Codable, Identifiable {
    let id: String
    let content: String
    let kind: String
    let source: String?
    let evidence: String?
    let replaces: String?      // the memory this one would correct, described in words
    let createdAt: String?
}

// POST 5500/api/memory/{id}/confirm | /retract
struct MemoryStatusBody: Codable {
    let note: String?
}

// One module's answer to the server check in Settings.
struct ProbeResult {
    let ok: Bool
    let ms: Int
    let detail: String?
}
