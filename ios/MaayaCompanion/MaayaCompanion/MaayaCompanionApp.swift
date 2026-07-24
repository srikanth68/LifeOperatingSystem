import SwiftUI
import BackgroundTasks
import UserNotifications

@main
struct MaayaCompanionApp: App {
    @Environment(\.scenePhase) private var scenePhase

    @State private var locationManager = LocationManager()
    @State private var calendarManager = CalendarManager()
    @State private var healthManager = HealthManager()
    @State private var syncManager: SyncManager
    @State private var auth: AuthService
    @State private var client: MaayaClient

    // Strong ref for the foreground-presentation delegate.
    private static let notificationDelegate = ForegroundNotificationDelegate()

    init() {
        UNUserNotificationCenter.current().delegate = Self.notificationDelegate
        let loc = LocationManager()
        let cal = CalendarManager()
        let health = HealthManager()
        let authService = AuthService()

        _locationManager = State(initialValue: loc)
        _calendarManager = State(initialValue: cal)
        _healthManager = State(initialValue: health)
        _syncManager = State(initialValue: SyncManager(
            locationManager: loc,
            calendarManager: cal,
            healthManager: health
        ))
        _auth = State(initialValue: authService)
        _client = State(initialValue: MaayaClient(auth: authService))

        // Register background task
        SyncManager.registerBackgroundTask()
    }

    var body: some Scene {
        WindowGroup {
            ContentView(
                locationManager: locationManager,
                calendarManager: calendarManager,
                healthManager: healthManager,
                syncManager: syncManager,
                auth: auth,
                client: client
            )
            .preferredColorScheme(.dark)
            .tint(MaayaTheme.gold)
            .task {
                // Request permissions on first launch
                locationManager.requestAuthorization()
                await calendarManager.requestAccess()
                await healthManager.requestAuthorization()
                await NotificationManager.shared.requestAuthorization()
                if auth.isAuthenticated { await NotificationManager.shared.sync(using: client) }
            }
            .onChange(of: scenePhase) { _, phase in
                // Re-mirror reminders/alerts to local notifications each time the
                // app comes forward, catching anything created elsewhere meanwhile.
                if phase == .active, auth.isAuthenticated {
                    Task { await NotificationManager.shared.sync(using: client) }
                }
            }
            .onReceive(NotificationCenter.default.publisher(for: UIApplication.didEnterBackgroundNotification)) { _ in
                SyncManager.scheduleBackgroundSync()
            }
        }
    }
}
