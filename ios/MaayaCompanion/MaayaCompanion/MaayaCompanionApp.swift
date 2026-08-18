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
        // Must run before anything reads a setting.
        //
        // @AppStorage("autoSyncEnabled") = true is a READ-TIME fallback for that one
        // property wrapper; it never writes to UserDefaults. SyncManager's background
        // handler reads the same key with UserDefaults.standard.bool(forKey:), which
        // answers false for a key that was never written -- so background refresh exited
        // immediately on every run while Settings displayed the toggle as on, and the
        // Vitara push never fired either. Nothing logged, because nothing failed.
        //
        // register(defaults:) populates NSRegistrationDomain, which both @AppStorage and
        // a bare UserDefaults read consult, so the switch and the worker finally agree.
        UserDefaults.standard.register(defaults: [
            "autoSyncEnabled": true,
            "vitaraSyncEnabled": true,
            "serverHost": "localhost",
            "serverScheme": "http",
        ])

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
                // Also schedule at launch, not only on backgrounding: a fresh install
                // that has never been backgrounded has no pending request at all, so the
                // first refresh would wait for the user to happen to swipe away.
                // Submitting the same identifier twice replaces, so this is safe to repeat.
                SyncManager.scheduleBackgroundSync()
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
