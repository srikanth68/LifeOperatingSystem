import Foundation

// NorthStar's cross-module action queue — GET 5500/api/actions?status=pending
//
// dueDate is a String, not a Date, and deliberately so: the server serialises it as
// `a.DueDate?.ToString("yyyy-MM-dd")`, a bare day with no time and no zone. Decoding
// that as a Date would throw against any ISO-8601 strategy and take the whole list
// down with it.
struct ActionItem: Codable, Identifiable, Equatable {
    let id: String
    let source: String?
    let category: String?
    let title: String
    let description: String?
    let priority: Int
    let dueDate: String?
    let status: String
    let createdAt: Date

    // 1 is urgent, 5 is someday — NorthStar's convention, not a score.
    var isUrgent: Bool { priority <= 2 }
}

struct UpdateActionBody: Codable {
    let status: String
    let resolvedBy: String?
}

// POST 5600/api/habits/{id}/log — Karma keys by habit + date, so a repeat corrects.
struct HabitLogBody: Codable {
    let date: String?
    let completed: Bool
    let note: String?
}

// PUT 5300/api/reminders/{id} — a full replace, not a patch: omitting text blanks it.
struct ReminderUpsertBody: Codable {
    let text: String
    let dueAt: Date
    let notifyTelegram: Bool
    let done: Bool
}
