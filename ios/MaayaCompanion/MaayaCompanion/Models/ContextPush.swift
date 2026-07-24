import Foundation

struct ContextPushRequest: Codable {
    let location: LocationPayload?
    let calendarEvents: [CalendarEventPayload]?
    let health: HealthPayload?
    let timestamp: Date
}

struct LocationPayload: Codable {
    let latitude: Double
    let longitude: Double
    let address: String?
}

struct CalendarEventPayload: Codable {
    let title: String
    let startTime: Date
    let endTime: Date
    let location: String?
    let allDay: Bool
}

struct HealthPayload: Codable {
    let steps: Int?
    let heartRate: Int?
    let activeCalories: Int?
    let sleepHours: Double?
    let sleepStart: Date?
    let sleepEnd: Date?
    let rawJson: String?
}

struct ContextPushResult: Codable {
    let received: Bool
    let message: String
}

// Payload for the direct Vitara HealthKit ingest endpoint (POST /api/healthkit/ingest).
// The base snapshot (steps…sleepEnd + timestamp) is unchanged and stays
// backward-compatible with the existing context-push flow. The trailing fields
// are optional richer data (weight, recent workouts, multi-day step/calorie
// history) added for the deeper Vitara sync; older servers simply ignore them.
struct HealthKitIngestRequest: Codable {
    let steps: Int?
    let heartRate: Int?
    let activeCalories: Int?
    let sleepHours: Double?
    let sleepStart: Date?
    let sleepEnd: Date?
    let timestamp: Date
    let weightKg: Double?
    let workouts: [WorkoutPayload]?
    let dailyActivity: [DailyActivityPayload]?
}

struct WorkoutPayload: Codable {
    let activity: String
    let calories: Int?
    let distanceMeters: Int?
    let start: Date
    let end: Date
    let intensity: String?
}

struct DailyActivityPayload: Codable {
    let day: String        // yyyy-MM-dd
    let steps: Int?
    let activeCalories: Int?
}

// Everything HealthManager gathers for a full Vitara sync in one pull.
struct HealthKitBundle {
    let snapshot: HealthPayload
    let weightKg: Double?
    let workouts: [WorkoutPayload]
    let dailyActivity: [DailyActivityPayload]
}

struct HealthKitIngestResult: Codable {
    let received: Bool
    let applied: [String]
}
