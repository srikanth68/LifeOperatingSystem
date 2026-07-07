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
    let rawJson: String?
}

struct ContextPushResult: Codable {
    let received: Bool
    let message: String
}
