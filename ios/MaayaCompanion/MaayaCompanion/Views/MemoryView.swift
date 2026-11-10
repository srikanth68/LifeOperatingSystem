import SwiftUI

// What San knows about you, and the means to correct it.
//
// This is not a dashboard. Every section except Health can be acted on: a wrong fact or a
// junk memory is deleted with a swipe, a stale insight dismissed. Left alone, a bad
// memory gets recalled into San's context again and again -- fixing it here is the
// cheapest way to make San's answers better.
//
// Five sources, fetched together, each allowed to fail on its own: NorthStar's review
// queue, facts, recent memories and insights, and Vitara Insight's active health findings.
//
// The review queue comes first because it is the one that grows on its own. San's chat
// distillation and the email reader both PROPOSE memories rather than asserting them,
// and a proposed memory is recalled marked "unconfirmed" until someone says yes or no.
// Confirming makes San state it as fact; retracting stops it being recalled while
// keeping the record of the mistake, which is what Delete further down does not.
struct MemoryView: View {
    let client: MaayaClient

    @State private var review: [ReviewMemoryItem] = []
    @State private var facts: [UserFactItem] = []
    @State private var memories: [MemoryItem] = []
    @State private var insights: [NorthStarInsight] = []
    @State private var findings: [HealthFinding] = []
    @State private var loading = false
    @State private var errorText: String?
    @State private var busy: Set<String> = []
    @State private var showVoiceNote = false

    var body: some View {
        NavigationStack {
            List {
                if let errorText {
                    Section {
                        Label(errorText, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.orange)
                    }
                }

                if !review.isEmpty {
                    Section {
                        ForEach(review) { m in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(m.content)
                                if let replaces = m.replaces, !replaces.isEmpty {
                                    Text("Corrects: \(replaces)")
                                        .font(.caption)
                                        .foregroundStyle(MaayaTheme.gold)
                                }
                                Text(reviewCaption(m))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                                    .lineLimit(2)
                            }
                            .opacity(busy.contains(m.id) ? 0.5 : 1)
                            .swipeActions(edge: .leading) {
                                Button("Confirm") {
                                    Task { await run(id: m.id) { try await client.confirmMemory(m.id) } }
                                }
                                .tint(.green)
                            }
                            .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                                Button("Retract") {
                                    Task { await run(id: m.id) { try await client.retractMemory(m.id) } }
                                }
                                .tint(.orange)
                            }
                        }
                    } header: {
                        Text("To review (\(review.count))")
                    } footer: {
                        Text("Swipe right to confirm, left to retract.")
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
                    ForEach(settledMemories) { m in
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
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { showVoiceNote = true } label: {
                        Image(systemName: "mic.badge.plus")
                    }
                    .accessibilityLabel("Voice note")
                }
            }
            .sheet(isPresented: $showVoiceNote) {
                VoiceNoteView(client: client) { Task { await load() } }
            }
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

    // Recent memories minus the ones already shown for review above, so a proposed
    // memory is not listed twice with two different sets of swipes.
    private var settledMemories: [MemoryItem] {
        let pending = Set(review.map(\.id))
        return memories.filter { !pending.contains($0.id) }
    }

    // "event · email · 3 Nov", then why it was proposed, when the proposer said.
    private func reviewCaption(_ m: ReviewMemoryItem) -> String {
        var parts = [m.kind]
        if let source = m.source, !source.isEmpty { parts.append(source) }
        if let day = Self.shortDate(m.createdAt) { parts.append(day) }
        var caption = parts.joined(separator: " · ")
        if let evidence = m.evidence, !evidence.isEmpty { caption += "\n\(evidence)" }
        return caption
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

        async let rv = client.memoryReview()
        async let fa = client.facts()
        async let me = client.recentMemories()
        async let ins = client.insights()
        async let fi = client.healthFindings()

        let rr = try? await rv
        let ff = try? await fa
        let mm = try? await me
        let ii = try? await ins
        let hh = try? await fi

        review   = rr?.memories ?? review
        facts    = (ff ?? facts).sorted { $0.key < $1.key }
        memories = mm ?? memories
        insights = ii ?? insights
        findings = hh ?? findings

        let down = [rr == nil ? "review queue" : nil,
                    ff == nil ? "facts" : nil,
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
