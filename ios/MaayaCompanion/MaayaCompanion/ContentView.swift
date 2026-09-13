import SwiftUI

// Five tabs: San, Now, Nexus, Memory, Settings.
//
// The old Dashboard / Status / Health tabs were dropped as read-only reporting -- net
// worth, module latency, sleep scores -- true, but no reason to pick up a phone.
//
// The two added since earn their place differently. Memory is where a wrong fact or a
// junk memory gets deleted before San recalls it again. Nexus is the one read-only screen,
// kept because a glance at the watchlist is exactly what a phone is picked up for.
//
// The initialiser still takes the managers it always did, even though only Settings reads
// most of them now -- SyncManager holds references to all three, and unpicking that is a
// separate change from re-pointing the app at actions.
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
                Tab("San", systemImage: "bubble.left.and.bubble.right.fill") {
                    ChatView(client: client)
                }

                Tab("Now", systemImage: "checklist") {
                    NowView(client: client)
                }

                Tab("Nexus", systemImage: "chart.line.uptrend.xyaxis") {
                    NexusView(client: client)
                }

                Tab("Memory", systemImage: "brain.head.profile") {
                    MemoryView(client: client)
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
