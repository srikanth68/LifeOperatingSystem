import SwiftUI

struct HealthSummaryView: View {
    let healthManager: HealthManager
    @State private var healthData: HealthPayload?
    @State private var isLoading = false

    var body: some View {
        NavigationStack {
            ScrollView {
                if !healthManager.isAvailable {
                    ContentUnavailableView {
                        Label("HealthKit Unavailable", systemImage: "heart.slash")
                    } description: {
                        Text("Health data is not available on this device.")
                    }
                } else if !healthManager.isAuthorized {
                    ContentUnavailableView {
                        Label("Health Access Required", systemImage: "lock.shield")
                    } description: {
                        Text(healthManager.errorMessage ?? "Grant health access in Settings to view your data.")
                    }
                } else if isLoading {
                    ProgressView("Loading health data...")
                        .padding(.top, 60)
                } else {
                    LazyVGrid(columns: [
                        GridItem(.flexible()),
                        GridItem(.flexible())
                    ], spacing: 16) {
                        HealthCard(
                            title: "Steps",
                            value: healthData?.steps.map { "\($0.formatted())" } ?? "--",
                            icon: "figure.walk",
                            color: .green
                        )

                        HealthCard(
                            title: "Heart Rate",
                            value: healthData?.heartRate.map { "\($0) bpm" } ?? "--",
                            icon: "heart.fill",
                            color: .red
                        )

                        HealthCard(
                            title: "Active Calories",
                            value: healthData?.activeCalories.map { "\($0) kcal" } ?? "--",
                            icon: "flame.fill",
                            color: .orange
                        )

                        HealthCard(
                            title: "Sleep",
                            value: healthData?.sleepHours.map { String(format: "%.1f hrs", $0) } ?? "--",
                            icon: "moon.fill",
                            color: .indigo
                        )
                    }
                    .padding()
                }
            }
            .navigationTitle("Health")
            .task {
                await refreshData()
            }
            .refreshable {
                await refreshData()
            }
        }
    }

    private func refreshData() async {
        isLoading = true
        healthData = await healthManager.fetchTodayData()
        isLoading = false
    }
}

struct HealthCard: View {
    let title: String
    let value: String
    let icon: String
    let color: Color

    var body: some View {
        VStack(spacing: 12) {
            Image(systemName: icon)
                .font(.title)
                .foregroundStyle(color)

            Text(value)
                .font(.title2)
                .fontWeight(.bold)
                .lineLimit(1)
                .minimumScaleFactor(0.7)

            Text(title)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding()
        .background(Color(.systemGray6))
        .clipShape(RoundedRectangle(cornerRadius: 16))
    }
}
