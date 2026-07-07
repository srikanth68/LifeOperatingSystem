import Foundation
import EventKit

@Observable
final class CalendarManager {
    var isAuthorized = false
    var errorMessage: String?

    private let store = EKEventStore()

    func requestAccess() async {
        do {
            let granted = try await store.requestFullAccessToEvents()
            isAuthorized = granted
            if !granted {
                errorMessage = "Calendar access denied. Go to Settings > Privacy > Calendars to enable."
            }
        } catch {
            errorMessage = "Calendar access error: \(error.localizedDescription)"
            isAuthorized = false
        }
    }

    func fetchEvents(from startDate: Date = .now, to endDate: Date? = nil) -> [CalendarEventPayload] {
        guard isAuthorized else { return [] }

        let end = endDate ?? Calendar.current.date(byAdding: .day, value: 7, to: startDate)!
        let predicate = store.predicateForEvents(withStart: startDate, end: end, calendars: nil)
        let events = store.events(matching: predicate)

        return events.map { event in
            CalendarEventPayload(
                title: event.title ?? "Untitled",
                startTime: event.startDate,
                endTime: event.endDate,
                location: event.location,
                allDay: event.isAllDay
            )
        }
    }
}
