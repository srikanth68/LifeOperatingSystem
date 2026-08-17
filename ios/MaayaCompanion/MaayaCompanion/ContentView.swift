import SwiftUI

// Three tabs, down from five.
//
// The old set was Dashboard / San / Status / Health / Settings -- three of the five were
// read-only reporting: net worth, module latency, sleep scores. All true, none of them a
// reason to pick up a phone. The website is the better place to look at the system; the
// phone is where something gets done about it.
//
// So: talk to San, act on what is outstanding, and configure. Nothing else.
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
