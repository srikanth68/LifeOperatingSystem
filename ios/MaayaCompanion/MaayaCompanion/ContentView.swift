import SwiftUI

struct ContentView: View {
    let locationManager: LocationManager
    let calendarManager: CalendarManager
    let healthManager: HealthManager
    let syncManager: SyncManager

    var body: some View {
        TabView {
            Tab("Status", systemImage: "antenna.radiowaves.left.and.right") {
                StatusView(
                    locationManager: locationManager,
                    syncManager: syncManager
                )
            }

            Tab("Health", systemImage: "heart.fill") {
                HealthSummaryView(healthManager: healthManager)
            }

            Tab("Settings", systemImage: "gear") {
                SettingsView(
                    locationManager: locationManager,
                    calendarManager: calendarManager,
                    healthManager: healthManager
                )
            }
        }
    }
}
