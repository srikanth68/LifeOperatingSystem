import SwiftUI
import MapKit

struct StatusView: View {
    let locationManager: LocationManager
    let syncManager: SyncManager

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 20) {
                    // Sync Status Card
                    syncStatusCard

                    // Location Map Card
                    locationCard

                    // Sync Now Button
                    Button {
                        Task { await syncManager.syncNow() }
                    } label: {
                        HStack {
                            if syncManager.syncStatus == .syncing {
                                ProgressView()
                                    .tint(.white)
                            } else {
                                Image(systemName: "arrow.triangle.2.circlepath")
                            }
                            Text("Sync Now")
                                .fontWeight(.semibold)
                        }
                        .frame(maxWidth: .infinity)
                        .padding()
                        .background(syncManager.syncStatus == .syncing ? Color.gray : Color.accentColor)
                        .foregroundStyle(.white)
                        .clipShape(RoundedRectangle(cornerRadius: 12))
                    }
                    .disabled(syncManager.syncStatus == .syncing)

                    // Error Message
                    if let error = syncManager.lastError {
                        HStack {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .foregroundStyle(.yellow)
                            Text(error)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        .padding()
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color(.systemGray6))
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                    }
                }
                .padding()
            }
            .navigationTitle("Maaya")
        }
    }

    private var syncStatusCard: some View {
        VStack(spacing: 12) {
            // Status Icon
            Image(systemName: syncStatusIcon)
                .font(.system(size: 48))
                .foregroundStyle(syncStatusColor)

            // Status Text
            Text(syncManager.syncStatus.rawValue)
                .font(.headline)

            // Last Sync Time
            if let lastSync = syncManager.lastSyncTime {
                Text("Last sync: \(lastSync.formatted(.relative(presentation: .named)))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Text("Never synced")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            // Server URL
            let serverURL = UserDefaults.standard.string(forKey: "serverURL") ?? "http://localhost:5300"
            HStack {
                Image(systemName: "server.rack")
                    .font(.caption)
                Text(serverURL)
                    .font(.caption)
                    .lineLimit(1)
            }
            .foregroundStyle(.secondary)
        }
        .padding()
        .frame(maxWidth: .infinity)
        .background(Color(.systemGray6))
        .clipShape(RoundedRectangle(cornerRadius: 16))
    }

    private var locationCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label("Location", systemImage: "location.fill")
                .font(.headline)

            if let location = locationManager.currentLocation {
                Map(initialPosition: .region(MKCoordinateRegion(
                    center: location.coordinate,
                    span: MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)
                ))) {
                    Marker("You", coordinate: location.coordinate)
                }
                .frame(height: 180)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .allowsHitTesting(false)

                if let address = locationManager.currentAddress {
                    Text(address)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            } else {
                ContentUnavailableView {
                    Label("No Location", systemImage: "location.slash")
                } description: {
                    Text(locationManager.errorMessage ?? "Waiting for location data...")
                }
                .frame(height: 180)
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(.systemGray6))
        .clipShape(RoundedRectangle(cornerRadius: 16))
    }

    private var syncStatusIcon: String {
        switch syncManager.syncStatus {
        case .idle: "circle.dashed"
        case .syncing: "arrow.triangle.2.circlepath"
        case .success: "checkmark.circle.fill"
        case .error: "xmark.circle.fill"
        }
    }

    private var syncStatusColor: Color {
        switch syncManager.syncStatus {
        case .idle: .secondary
        case .syncing: .blue
        case .success: .green
        case .error: .red
        }
    }
}
