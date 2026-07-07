import Foundation
import BackgroundTasks

enum SyncStatus: String {
    case idle = "Idle"
    case syncing = "Syncing..."
    case success = "Success"
    case error = "Error"
}

@Observable
final class SyncManager {
    var lastSyncTime: Date?
    var syncStatus: SyncStatus = .idle
    var lastError: String?

    static let backgroundTaskIdentifier = "com.maaya.companion.sync"

    private let locationManager: LocationManager
    private let calendarManager: CalendarManager
    private let healthManager: HealthManager
    private let apiClient: APIClient

    init(
        locationManager: LocationManager,
        calendarManager: CalendarManager,
        healthManager: HealthManager,
        apiClient: APIClient = APIClient()
    ) {
        self.locationManager = locationManager
        self.calendarManager = calendarManager
        self.healthManager = healthManager
        self.apiClient = apiClient
    }

    func syncNow() async {
        syncStatus = .syncing
        lastError = nil

        do {
            // Gather data from all managers
            let location = locationManager.toPayload()
            let events = calendarManager.fetchEvents()
            let health = await healthManager.fetchTodayData()

            let request = ContextPushRequest(
                location: location,
                calendarEvents: events.isEmpty ? nil : events,
                health: health,
                timestamp: .now
            )

            let result = try await apiClient.pushContext(request)

            if result.received {
                syncStatus = .success
                lastSyncTime = .now
                lastError = nil
            } else {
                syncStatus = .error
                lastError = result.message
            }
        } catch {
            syncStatus = .error
            lastError = error.localizedDescription
        }
    }

    // MARK: - Background Task Registration

    static func registerBackgroundTask() {
        BGTaskScheduler.shared.register(
            forTaskWithIdentifier: backgroundTaskIdentifier,
            using: nil
        ) { task in
            guard let refreshTask = task as? BGAppRefreshTask else { return }
            handleBackgroundRefresh(refreshTask)
        }
    }

    static func scheduleBackgroundSync() {
        let request = BGAppRefreshTaskRequest(identifier: backgroundTaskIdentifier)
        request.earliestBeginDate = Date(timeIntervalSinceNow: 15 * 60) // ~15 minutes
        do {
            try BGTaskScheduler.shared.submit(request)
        } catch {
            print("Failed to schedule background sync: \(error)")
        }
    }

    private static func handleBackgroundRefresh(_ task: BGAppRefreshTask) {
        // Schedule the next refresh
        scheduleBackgroundSync()

        let autoSyncEnabled = UserDefaults.standard.bool(forKey: "autoSyncEnabled")
        guard autoSyncEnabled else {
            task.setTaskCompleted(success: true)
            return
        }

        // Create managers for background sync
        let locationManager = LocationManager()
        let calendarManager = CalendarManager()
        let healthManager = HealthManager()
        let apiClient = APIClient()

        let syncManager = SyncManager(
            locationManager: locationManager,
            calendarManager: calendarManager,
            healthManager: healthManager,
            apiClient: apiClient
        )

        let syncTask = Task {
            await syncManager.syncNow()
            task.setTaskCompleted(success: syncManager.syncStatus == .success)
        }

        task.expirationHandler = {
            syncTask.cancel()
        }
    }
}
