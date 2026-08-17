import Foundation

// San reminders/alerts, read for local-notification scheduling.
// Shapes match San's ReminderResult / AlertResult DTOs (port 5300).

struct ReminderItem: Codable, Identifiable {
    let id: String
    let text: String
    let dueAt: Date
    let done: Bool
    let notifyTelegram: Bool
    let notifiedAt: Date?
    let createdAt: Date
}

struct AlertItem: Codable, Identifiable {
    let id: String
    let type: String
    let title: String
    let description: String
    let thresholdValue: Double?
    let triggerAt: Date?
    let active: Bool
    let notifyTelegram: Bool
    let triggeredAt: Date?
    let createdAt: Date
}

// POST 5300/api/reminders. dueAt encodes as ISO-8601 UTC (MaayaJSON.encoder), which is
// what the reminder worker compares against DateTime.UtcNow — so build the Date from the
// user's local wall clock and let the encoder do the conversion.
struct CreateReminderBody: Codable {
    let text: String
    let dueAt: Date
    let notifyTelegram: Bool
}
