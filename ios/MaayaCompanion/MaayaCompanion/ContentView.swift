import SwiftUI

struct ContentView: View {
    let locationManager: LocationManager
    let calendarManager: CalendarManager
    let healthManager: HealthManager
    let syncManager: SyncManager
    let auth: AuthService
    let client: MaayaClient

    var body: some View {
        if auth.isAuthenticated {
            TabView {
                Tab("Dashboard", systemImage: "square.grid.2x2.fill") {
                    DashboardView(client: client)
                }

                Tab("San", systemImage: "bubble.left.and.bubble.right.fill") {
                    ChatView(client: client)
                }

                Tab("Status", systemImage: "antenna.radiowaves.left.and.right") {
                    StatusView(
                        locationManager: locationManager,
                        syncManager: syncManager,
                        healthManager: healthManager
                    )
                }

                Tab("Health", systemImage: "heart.fill") {
                    HealthSummaryView(healthManager: healthManager)
                }

                Tab("Settings", systemImage: "gear") {
                    SettingsView(
                        locationManager: locationManager,
                        calendarManager: calendarManager,
                        healthManager: healthManager,
                        auth: auth
                    )
                }
            }
        } else {
            LoginView(auth: auth)
        }
    }
}
