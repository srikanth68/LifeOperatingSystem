import SwiftUI

// Sentinel's watchlist, as the website's Nexus > Watchlist tab shows it: every tracked
// symbol with its price, move, verdict and conviction, highest conviction first.
//
// Read-only on purpose. Nexus reads Sentinel's database and never writes to it, and a
// phone is the wrong place to start acting on a trading verdict anyway -- this is for
// glancing at the desk, not trading from it.
struct NexusView: View {
    let client: MaayaClient

    @State private var board: [NexusBoardRow] = []
    @State private var watch: [NexusWatchItem] = []
    @State private var status: NexusStatus?
    @State private var errorText: String?
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            List {
                if let status {
                    Section {
                        HStack(spacing: 14) {
                            Label(status.marketOpen ? "Open" : "Closed", systemImage: "circle.fill")
                                .foregroundStyle(status.marketOpen ? MaayaTheme.cash : .secondary)
                            Text("Ran \(Self.relative(status.lastRunAt))")
                            Spacer()
                            Text("\(status.openAlerts24h) alerts")
                        }
                        .font(.caption)
                    }
                }

                if let errorText {
                    Section {
                        Label(errorText, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.orange)
                    }
                }

                if !board.isEmpty {
                    Section("Watchlist") {
                        ForEach(sortedBoard) { row($0) }
                    }
                }

                if !notScreened.isEmpty {
                    Section("Added, not screened yet") {
                        ForEach(notScreened) { w in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(w.symbol).font(.headline.monospaced())
                                Text([w.origin, w.note].compactMap { $0 }.joined(separator: " · "))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                        }
                    }
                }

                if loaded && board.isEmpty && watch.isEmpty && errorText == nil {
                    Section {
                        Label("Sentinel hasn't screened anything yet. That's normal before market open.",
                              systemImage: "list.bullet.rectangle")
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .navigationTitle("Nexus")
            .refreshable { await load() }
            // Refreshes while the tab is on screen, at the website's 45s cadence. The task
            // is cancelled when the tab is left, so a backgrounded app polls nothing.
            .task {
                while !Task.isCancelled {
                    await load()
                    try? await Task.sleep(for: .seconds(45))
                }
            }
        }
    }

    // MARK: - Pieces

    private func row(_ r: NexusBoardRow) -> some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text(r.symbol).font(.headline.monospaced())
                    Text(r.action.uppercased())
                        .font(.caption2.bold())
                        .padding(.horizontal, 7)
                        .padding(.vertical, 2)
                        .background(actionColor(r.action).opacity(0.2), in: Capsule())
                        .foregroundStyle(actionColor(r.action))
                }
                Text("Conviction \(r.conviction)/10 · \(r.freshness)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 4) {
                Text(r.price, format: .currency(code: "USD"))
                    .font(.body.monospacedDigit())
                Text(r.changePct.map { String(format: "%+.2f%%", $0) } ?? "—")
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(changeColor(r.changePct))
            }
        }
    }

    private func actionColor(_ action: String) -> Color {
        let a = action.lowercased()
        if a.contains("buy") { return MaayaTheme.cash }
        if a.contains("sell") || a.contains("avoid") { return .red }
        return MaayaTheme.gold
    }

    private func changeColor(_ pct: Double?) -> Color {
        guard let pct, pct != 0 else { return .secondary }
        return pct > 0 ? MaayaTheme.cash : .red
    }

    private var sortedBoard: [NexusBoardRow] {
        board.sorted { $0.conviction != $1.conviction ? $0.conviction > $1.conviction : $0.symbol < $1.symbol }
    }

    // Watchlist entries Sentinel has not produced a verdict for yet.
    private var notScreened: [NexusWatchItem] {
        let screened = Set(board.map(\.symbol))
        return watch.filter { !screened.contains($0.symbol) }
    }

    private static let relativeFormatter: RelativeDateTimeFormatter = {
        let f = RelativeDateTimeFormatter()
        f.unitsStyle = .short
        return f
    }()

    private static func relative(_ date: Date?) -> String {
        guard let date else { return "never" }
        return relativeFormatter.localizedString(for: date, relativeTo: .now)
    }

    // MARK: - Data

    private func load() async {
        async let b = client.nexusBoard()
        async let w = client.nexusWatchlist()
        async let s = client.nexusStatus()

        do {
            board = try await b
            errorText = nil
        } catch {
            errorText = Self.describe(error)
        }
        watch = (try? await w) ?? watch
        status = try? await s
        loaded = true
    }

    // 503 is Nexus saying Sentinel's database is not there yet, which is a state, not a fault.
    private static func describe(_ error: Error) -> String {
        if case let APIError.serverError(code, _) = error, code == 503 {
            return "Sentinel hasn't written its first cycle yet."
        }
        return "Couldn't reach Nexus: \(error.localizedDescription)"
    }
}
