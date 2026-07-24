import SwiftUI

// Read-only cross-module dashboard. One section per Maaya module, each loading
// its own summary from the authenticated API. Purely a viewer — no editing.
struct DashboardView: View {
    let client: MaayaClient

    @State private var refreshToken = UUID()

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 16) {
                    ModuleSection(title: "Vault", subtitle: "Net worth", icon: "banknote.fill",
                                  accent: MaayaTheme.gold, token: refreshToken,
                                  loader: client.vaultSummary) { VaultCard(summary: $0) }

                    ModuleSection(title: "Vitara", subtitle: "Health", icon: "heart.fill",
                                  accent: MaayaTheme.vitara, token: refreshToken,
                                  loader: client.vitaraDashboard) { VitaraCard(data: $0) }

                    ModuleSection(title: "Aasthi", subtitle: "Properties", icon: "house.fill",
                                  accent: MaayaTheme.gold, token: refreshToken,
                                  loader: client.aasthiSummary) { AasthiCard(summary: $0) }

                    ModuleSection(title: "Karma", subtitle: "Habits today", icon: "checkmark.seal.fill",
                                  accent: MaayaTheme.cash, token: refreshToken,
                                  loader: client.karmaToday) { KarmaCard(habits: $0) }

                    ModuleSection(title: "NorthStar", subtitle: "Knowledge", icon: "brain.head.profile",
                                  accent: MaayaTheme.goldLight, token: refreshToken,
                                  loader: client.northStarDashboard) { NorthStarCard(data: $0) }

                    ModuleSection(title: "Sutra", subtitle: "Documents", icon: "doc.fill",
                                  accent: MaayaTheme.gold, token: refreshToken,
                                  loader: client.sutraStats) { SutraCard(stats: $0) }

                    ModuleSection(title: "Nexus", subtitle: "Markets", icon: "chart.line.uptrend.xyaxis",
                                  accent: MaayaTheme.cash, token: refreshToken,
                                  loader: client.nexusStatus) { NexusCard(status: $0, client: client) }
                }
                .padding()
            }
            .navigationTitle("Dashboard")
            .refreshable { refreshToken = UUID() }
        }
    }
}

// MARK: - Generic loading section

private enum LoadState<T> {
    case loading, loaded(T), failed(String)
}

private struct ModuleSection<T, Content: View>: View {
    let title: String
    let subtitle: String
    let icon: String
    let accent: Color
    let token: UUID
    let loader: () async throws -> T
    @ViewBuilder let content: (T) -> Content

    @State private var state: LoadState<T> = .loading

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Label(title, systemImage: icon)
                    .font(.headline)
                    .foregroundStyle(accent)
                Spacer()
                Text(subtitle.uppercased())
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }

            switch state {
            case .loading:
                HStack { ProgressView().tint(accent); Text("Loading…").font(.caption).foregroundStyle(.secondary) }
                    .frame(maxWidth: .infinity, minHeight: 44)
            case .loaded(let value):
                content(value)
            case .failed(let message):
                HStack(spacing: 8) {
                    Image(systemName: "wifi.slash").foregroundStyle(.orange)
                    Text(message).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(accent: accent)
        .task(id: token) { await load() }
    }

    private func load() async {
        state = .loading
        do {
            state = .loaded(try await loader())
        } catch {
            state = .failed(error.localizedDescription)
        }
    }
}

// MARK: - Shared formatting

enum Fmt {
    static func money(_ v: Double) -> String {
        let sign = v < 0 ? "-" : ""
        let n = NSNumber(value: abs(v).rounded())
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.maximumFractionDigits = 0
        return "\(sign)$\(f.string(from: n) ?? "0")"
    }
    static func int(_ v: Double?) -> String {
        guard let v else { return "—" }
        let f = NumberFormatter(); f.numberStyle = .decimal; f.maximumFractionDigits = 0
        return f.string(from: NSNumber(value: v.rounded())) ?? "—"
    }
}

private struct Stat: View {
    let label: String
    let value: String
    var color: Color = .primary
    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(value).font(.title3).fontWeight(.bold).foregroundStyle(color).lineLimit(1).minimumScaleFactor(0.6)
            Text(label.uppercased()).font(.caption2).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// MARK: - Per-module cards

private struct VaultCard: View {
    let summary: VaultSummary
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(Fmt.money(summary.netWorth))
                .font(.system(size: 30, weight: .bold, design: .rounded))
                .foregroundStyle(MaayaTheme.gold)
            HStack {
                Stat(label: "Cash", value: Fmt.money(summary.totalCash), color: MaayaTheme.cash)
                Stat(label: "Debt", value: Fmt.money(summary.totalDebt), color: .red)
            }
        }
    }
}

private struct VitaraCard: View {
    let data: VitaraDashboard
    var body: some View {
        HStack {
            Stat(label: "Sleep score", value: Fmt.int(data.sleep?.score), color: .indigo)
            Stat(label: "Readiness", value: Fmt.int(data.readiness?.score), color: MaayaTheme.vitara)
            Stat(label: "Steps", value: Fmt.int(data.activity?.steps), color: MaayaTheme.cash)
        }
    }
}

private struct AasthiCard: View {
    let summary: AasthiSummary
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(Fmt.money(summary.totalCurrentValue))
                .font(.system(size: 26, weight: .bold, design: .rounded))
            HStack {
                Stat(label: "Properties", value: "\(summary.propertyCount)")
                Stat(label: "Profit", value: Fmt.money(summary.totalProfit),
                     color: summary.totalProfit >= 0 ? MaayaTheme.cash : .red)
                if let pct = summary.totalProfitPct {
                    Stat(label: "Return", value: String(format: "%.1f%%", pct),
                         color: pct >= 0 ? MaayaTheme.cash : .red)
                }
            }
        }
    }
}

private struct KarmaCard: View {
    let habits: [KarmaHabit]
    private var done: Int { habits.filter { $0.todayCompleted == true }.count }
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if habits.isEmpty {
                Text("No active habits.").font(.caption).foregroundStyle(.secondary)
            } else {
                Text("\(done)/\(habits.count) done today")
                    .font(.title3).fontWeight(.bold).foregroundStyle(MaayaTheme.cash)
                ForEach(habits.prefix(4)) { h in
                    HStack {
                        Text(h.emoji)
                        Text(h.name).font(.subheadline).lineLimit(1)
                        Spacer()
                        Text("🔥\(h.currentStreak)").font(.caption).foregroundStyle(.secondary)
                        Image(systemName: h.todayCompleted == true ? "checkmark.circle.fill" : "circle")
                            .foregroundStyle(h.todayCompleted == true ? MaayaTheme.cash : .secondary)
                    }
                }
            }
        }
    }
}

private struct NorthStarCard: View {
    let data: NorthStarDashboard
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("\(data.totalEntries) entries")
                .font(.title3).fontWeight(.bold).foregroundStyle(MaayaTheme.goldLight)
            if let insight = data.recentInsights.first {
                VStack(alignment: .leading, spacing: 2) {
                    Text(insight.title).font(.subheadline).fontWeight(.semibold).lineLimit(1)
                    Text(insight.body).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                }
            } else if let entry = data.recentEntries.first {
                Text("Latest: \(entry.summary)").font(.caption).foregroundStyle(.secondary).lineLimit(2)
            }
        }
    }
}

private struct SutraCard: View {
    let stats: SutraStats
    var body: some View {
        HStack {
            Stat(label: "Documents", value: "\(stats.totalCount)")
            Stat(label: "Size", value: stats.totalSize)
            Stat(label: "Expiring", value: "\(stats.expiringSoon)",
                 color: stats.expiringSoon > 0 ? .orange : .primary)
        }
    }
}

private struct NexusCard: View {
    let status: NexusStatus
    let client: MaayaClient
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Stat(label: "Tracked", value: "\(status.trackedCount)")
                Stat(label: "Alerts 24h", value: "\(status.openAlerts24h)",
                     color: status.openAlerts24h > 0 ? .orange : .primary)
                Stat(label: "Market", value: status.marketOpen ? "Open" : "Closed",
                     color: status.marketOpen ? MaayaTheme.cash : .secondary)
            }
            NavigationLink {
                NexusBoardView(client: client)
            } label: {
                Text("View board →").font(.caption).foregroundStyle(MaayaTheme.cash)
            }
        }
    }
}

// A small drill-in for the Sentinel board (still read-only).
private struct NexusBoardView: View {
    let client: MaayaClient
    @State private var rows: [NexusBoardRow] = []
    @State private var error: String?

    var body: some View {
        List {
            if let error {
                Text(error).font(.caption).foregroundStyle(.secondary)
            }
            ForEach(rows) { r in
                HStack {
                    Text(r.symbol).fontWeight(.semibold)
                    Spacer()
                    Text(Fmt.money(r.price)).foregroundStyle(.secondary)
                    Text(r.action)
                        .font(.caption).padding(.horizontal, 6).padding(.vertical, 2)
                        .background(MaayaTheme.surface, in: Capsule())
                    Text("C\(r.conviction)").font(.caption).foregroundStyle(.secondary)
                }
            }
        }
        .navigationTitle("Sentinel Board")
        .task {
            do { rows = try await client.nexusBoard() }
            catch { self.error = error.localizedDescription }
        }
    }
}
