import SwiftUI
import BackgroundTasks

@main
struct MaayaCompanionApp: App {
    @State private var locationManager = LocationManager()
    @State private var calendarManager = CalendarManager()
    @State private var healthManager = HealthManager()
    @State private var syncManager: SyncManager

    init() {
        let loc = LocationManager()
        let cal = CalendarManager()
        let health = HealthManager()

        _locationManager = State(initialValue: loc)
        _calendarManager = State(initialValue: cal)
        _healthManager = State(initialValue: health)
        _syncManager = State(initialValue: SyncManager(
            locationManager: loc,
            calendarManager: cal,
            healthManager: health
        ))

        // Register background task
        SyncManager.registerBackgroundTask()
    }

    var body: some Scene {
        WindowGroup {
            ContentView(
                locationManager: locationManager,
                calendarManager: calendarManager,
                healthManager: healthManager,
                syncManager: syncManager
            )
            .task {
                // Request permissions on first launch
                locationManager.requestAuthorization()
                await calendarManager.requestAccess()
                await healthManager.requestAuthorization()
            }
            .onReceive(NotificationCenter.default.publisher(for: UIApplication.didEnterBackgroundNotification)) { _ in
                SyncManager.scheduleBackgroundSync()
            }
        }
    }
}
