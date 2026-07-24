import SwiftUI

struct SettingsView: View {
    // Host + scheme are the single source of truth for every module URL; the
    // per-module port is applied automatically (see AppConfig / ModulePort).
    @AppStorage("serverHost") private var serverHost = "100.126.41.41"
    @AppStorage("serverScheme") private var serverScheme = "http"
    @AppStorage("apiKey") private var apiKey = ""
    @AppStorage("autoSyncEnabled") private var autoSyncEnabled = true
    @AppStorage("vitaraSyncEnabled") private var vitaraSyncEnabled = true

    let locationManager: LocationManager
    let calendarManager: CalendarManager
    let healthManager: HealthManager
    let auth: AuthService

    private let schemes = ["http", "https"]

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    LabeledContent("Signed in as", value: auth.username ?? "—")
                    Button("Sign Out", role: .destructive) {
                        Task { await auth.logout() }
                    }
                } header: {
                    Text("Account")
                } footer: {
                    Text("Auth tokens are stored in the iOS Keychain. Sign in uses your Maaya PIN on a trusted mesh network, or username/password otherwise.")
                        .font(.caption)
                }

                Section {
                    Picker("Scheme", selection: $serverScheme) {
                        ForEach(schemes, id: \.self) { Text($0) }
                    }
                    TextField("Host / mesh IP", text: $serverHost)
                        .keyboardType(.URL)
                        .autocorrectionDisabled()
                        .textInputAutocapitalization(.never)

                    SecureField("Device Key", text: $apiKey)
                        .textContentType(.password)
                        .autocorrectionDisabled()
                        .textInputAutocapitalization(.never)
                } header: {
                    Text("Server")
                } footer: {
                    Text("Reachable over NordVPN Meshnet or Tailscale (e.g. 100.126.41.41). Each module is served on its own port off this host. The Device Key matches DEVICE_API_KEY on the server for the telemetry/HealthKit uploads.")
                        .font(.caption)
                }

                Section("Sync") {
                    Toggle("Auto-Sync", isOn: $autoSyncEnabled)

                    if autoSyncEnabled {
                        Text("Background sync runs approximately every 15 minutes when the app is not active.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                Section {
                    Toggle("Sync Health to Vitara", isOn: $vitaraSyncEnabled)
                } header: {
                    Text("Apple Health → Vitara")
                } footer: {
                    Text("Pushes steps, heart rate, active calories, sleep, weight, recent workouts, and the last week of daily activity from Apple Health directly into Vitara (port 5100). Uses the Device Key above.")
                        .font(.caption)
                }

                Section("Permissions") {
                    permissionRow(
                        title: "Location",
                        icon: "location.fill",
                        status: locationPermissionStatus,
                        color: locationPermissionColor
                    )

                    permissionRow(
                        title: "Calendar",
                        icon: "calendar",
                        status: calendarManager.isAuthorized ? "Granted" : "Not Granted",
                        color: calendarManager.isAuthorized ? .green : .orange
                    )

                    permissionRow(
                        title: "Health",
                        icon: "heart.fill",
                        status: healthManager.isAuthorized ? "Granted" : (healthManager.isAvailable ? "Not Granted" : "Unavailable"),
                        color: healthManager.isAuthorized ? .green : (healthManager.isAvailable ? .orange : .red)
                    )
                }

                Section("About") {
                    LabeledContent("App", value: "MaayaCompanion")
                    LabeledContent("Version", value: "1.0.0")
                    LabeledContent("Bundle ID", value: "com.maaya.companion")
                }
            }
            .navigationTitle("Settings")
        }
    }

    private func permissionRow(title: String, icon: String, status: String, color: Color) -> some View {
        HStack {
            Image(systemName: icon)
                .foregroundStyle(color)
                .frame(width: 24)
            Text(title)
            Spacer()
            Text(status)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private var locationPermissionStatus: String {
        switch locationManager.authorizationStatus {
        case .authorizedAlways: "Always"
        case .authorizedWhenInUse: "When In Use"
        case .denied: "Denied"
        case .restricted: "Restricted"
        case .notDetermined: "Not Determined"
        @unknown default: "Unknown"
        }
    }

    private var locationPermissionColor: Color {
        switch locationManager.authorizationStatus {
        case .authorizedAlways: .green
        case .authorizedWhenInUse: .yellow
        case .denied, .restricted: .red
        default: .orange
        }
    }
}
