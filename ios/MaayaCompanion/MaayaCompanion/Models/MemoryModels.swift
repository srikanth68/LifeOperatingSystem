import Foundation

// What San knows, for the Memory tab. Dates stay strings here: these rows come from
// SQLite with a mix of zoned, naive and date-only timestamps, and one odd format should
// not fail the whole list. MaayaDate.parse turns them into dates where a view needs one.

// ── NorthStar — GET 5500/api/facts (UserFact[]) ──
struct UserFactItem: Codable, Identifiable {
    let key: String
    let value: String
    let source: String?
    let updatedAt: String?
    var id: String { key }
}

// ── NorthStar — GET 5500/api/memory/recent ──
// Mostly written by San's distillation worker, which reads new chat every 15 minutes
// and keeps the lines worth remembering.
struct MemoryItem: Codable, Identifiable {
    let id: String
    let content: String
    let kind: String
    let source: String?
    let importance: Int?
    let createdAt: String?
}

// ── NorthStar — GET 5500/api/insights (InsightResult[], undismissed only) ──
struct NorthStarInsight: Codable, Identifiable {
    let id: String
    let title: String
    let body: String
    let generatedBy: String?
    let createdAt: String?
}

// ── Vitara Insight — GET 5110/api/health/findings (active only) ──
// Severity and confidence are left out on purpose: the summary already says what
// matters, and a field whose JSON type changes should not blank the section.
struct HealthFinding: Codable, Identifiable {
    let key: String
    let type: String?
    let metric: String?
    let summary: String
    let daysRunning: Int?
    let daysSinceDetected: Int?
    var id: String { key }
}

// ── Nexus — GET 5700/api/nexus/sentinel/watchlist (WatchItem[]) ──
struct NexusWatchItem: Codable, Identifiable {
    let symbol: String
    let origin: String
    let note: String?
    let addedAt: String?
    var id: String { symbol }
}
