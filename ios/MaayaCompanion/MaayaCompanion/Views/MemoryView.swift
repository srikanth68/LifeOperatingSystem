import SwiftUI

// What San knows about you, and the means to correct it.
//
// This is not a dashboard. Every section except Health can be acted on: a wrong fact or a
// junk memory is deleted with a swipe, a stale insight dismissed. Left alone, a bad
// memory gets recalled into San's context again and again -- fixing it here is the
// cheapest way to make San's answers better.
//
// Four sources, fetched together, each allowed to fail on its own: NorthStar's facts,
// recent memories and insights, and Vitara Insight's active health findings.
struct MemoryView: View {
    let client: MaayaClient

    @State private var facts: [UserFactItem] = []
    @State private var memories: [MemoryItem] = []
    @State private var insights: [NorthStarInsight] = []
    @State private var findings: [HealthFinding] = []
    @State private var loading = false
    @State private var errorText: String?
    @State private var busy: Set<String> = []

    var body: some View {
        NavigationStack {
            List {
                if let errorText {
                    Section {
                        Label(errorText, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.orange)
                    }
                }

                if !findings.isEmpty {
                    Section("Health") {
                        ForEach(findings) { f in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(f.summary)
                                Text(caption(for: f))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                        }
                    }
                }

                if !insights.isEmpty {
                    Section("Insights") {
                        ForEach(insights) { i in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(i.title).font(.body)
                                if !i.body.isEmpty {
                                    Text(i.body)
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            .opacity(busy.contains(i.id) ? 0.5 : 1)
                            .swipeActions(edge: .trailing) {
                                Button("Dismiss") {
                                    Task { await run(id: i.id) { try await client.dismissInsight(i.id) } }
                                }
                                .tint(.indigo)
                            }
                        }
                    }
                }

                Section("Facts") {
                    if facts.isEmpty && !loading {
                        Text("No facts yet. San saves one when you tell it something stable about yourself.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    ForEach(facts) { f in
                        VStack(alignment: .leading, spacing: 2) {
                            Text(f.key.replacingOccurrences(of: "_", with: " "))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                            Text(f.value)
                        }
                        .opacity(busy.contains(f.id) ? 0.5 : 1)
                        // No full swipe: unlike a to-do, a deleted fact cannot be undone.
                        .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                            Button("Delete", role: .destructive) {
                                Task { await run(id: f.id) { try await client.deleteFact(f.key) } }
                            }
                        }
                    }
                }

                Section("Recent memories") {
                    if memories.isEmpty && !loading {
                        Text("Nothing remembered yet.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    ForEach(memories) { m in
                        VStack(alignment: .leading, spacing: 2) {
                            Text(m.content)
                            Text([m.kind, Self.shortDate(m.createdAt)].compactMap { $0 }.joined(separator: " · "))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        .opacity(busy.contains(m.id) ? 0.5 : 1)
                        .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                            Button("Delete", role: .destructive) {
                                Task { await run(id: m.id) { try await client.deleteMemory(m.id) } }
                            }
                        }
                    }
                }
            }
            .navigationTitle("Memory")
            .refreshable { await load() }
            .task { await load() }
        }
    }

    // MARK: - Pieces

    // "HRV · 4 days" -- and says so when a finding has stopped being re-detected, because
    // a stale finding read as current is worse than none.
    private func caption(for f: HealthFinding) -> String {
        var parts: [String] = []
        if let metric = f.metric { parts.append(metric.replacingOccurrences(of: "_", with: " ")) }
        if let days = f.daysRunning { parts.append(days == 1 ? "today" : "\(days) days") }
        if let since = f.daysSinceDetected, since > 1 { parts.append("last seen \(since) days ago") }
        return parts.joined(separator: " · ")
    }

    private static let dayMonth: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "d MMM"
        return f
    }()

    private static func shortDate(_ raw: String?) -> String? {
        guard let raw, let date = MaayaDate.parse(raw) else { return nil }
        return dayMonth.string(from: date)
    }

    // MARK: - Data

    private func load() async {
        loading = true
        defer { loading = false }

        async let fa = client.facts()
        async let me = client.recentMemories()
        async let ins = client.insights()
        async let fi = client.healthFindings()

        let ff = try? await fa
        let mm = try? await me
        let ii = try? await ins
        let hh = try? await fi

        facts    = (ff ?? facts).sorted { $0.key < $1.key }
        memories = mm ?? memories
        insights = ii ?? insights
        findings = hh ?? findings

        let down = [ff == nil ? "facts" : nil,
                    mm == nil ? "memories" : nil,
                    ii == nil ? "insights" : nil,
                    hh == nil ? "health findings" : nil].compactMap { $0 }
        errorText = down.isEmpty ? nil : "Couldn't reach: \(down.joined(separator: ", "))"
    }

    private func run(id: String, _ work: @escaping () async throws -> Void) async {
        busy.insert(id)
        defer { busy.remove(id) }
        do {
            try await work()
            await load()
        } catch {
            errorText = error.localizedDescription
        }
    }
}
