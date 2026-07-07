import SwiftUI

struct SettingsView: View {
    @AppStorage("serverURL") private var serverURL = "http://localhost:5300"
    @AppStorage("apiKey") private var apiKey = ""
    @AppStorage("autoSyncEnabled") private var autoSyncEnabled = true

    let locationManager: LocationManager
    let calendarManager: CalendarManager
    let healthManager: HealthManager

    var body: some View {
        NavigationStack {
            Form {
                Section("Server") {
                    TextField("Server URL", text: $serverURL)
                        .textContentType(.URL)
                        .keyboardType(.URL)
                        .autocorrectionDisabled()
                        .textInputAutocapitalization(.never)

                    SecureField("API Key", text: $apiKey)
                        .textContentType(.password)
                        .autocorrectionDisabled()
                        .textInputAutocapitalization(.never)
                }

                Section("Sync") {
                    Toggle("Auto-Sync", isOn: $autoSyncEnabled)

                    if autoSyncEnabled {
                        Text("Background sync runs approximately every 15 minutes when the app is not active.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
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
