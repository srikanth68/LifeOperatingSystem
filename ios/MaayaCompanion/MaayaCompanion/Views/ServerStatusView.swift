import SwiftUI

// Is Everest answering, module by module.
//
// The old Status tab was dropped as reporting nobody opens a phone for. Away from the
// laptop that changes: when San does not reply, the first question is whether it is San,
// one module behind it, or the mesh itself -- and the phone is the only thing to hand.
// So it lives one level down in Settings, not as a tab, and is only ever fetched when
// opened.
//
// Each probe is a cheap authenticated read on that module's own port, the same ones the
// MCP gateway's maaya_status uses. Every module failing at once means the mesh or the
// Mac, not eight separate outages, and the screen says so.
struct ServerStatusView: View {
    let client: MaayaClient

    private struct Module: Identifiable {
        let name: String
        let port: Int
        let path: String
        var id: String { name }
    }

    private static let modules = [
        Module(name: "San", port: ModulePort.san, path: "/api/reminders"),
        Module(name: "NorthStar", port: ModulePort.northstar, path: "/api/memory/stats"),
        Module(name: "Vitara", port: ModulePort.vitara, path: "/api/dashboard"),
        Module(name: "Vitara Insight", port: ModulePort.insight, path: "/api/health/summary"),
        Module(name: "Vault", port: ModulePort.vault, path: "/api/summary"),
        Module(name: "Aasthi", port: ModulePort.aasthi, path: "/api/properties"),
        Module(name: "Karma", port: ModulePort.karma, path: "/api/habits/today"),
        Module(name: "Sutra", port: ModulePort.sutra, path: "/api/documents/stats"),
        Module(name: "Nexus", port: ModulePort.nexus, path: "/api/nexus/sentinel/status"),
    ]

    @State private var results: [String: ProbeResult] = [:]
    @State private var voice: VoiceStatus?
    @State private var checking = false
    @State private var checkedAt: Date?

    var body: some View {
        List {
            if allDown {
                Section {
                    Label("Nothing is answering. Check Meshnet on this phone, and that Everest is awake and on the mesh.",
                          systemImage: "wifi.exclamationmark")
                        .foregroundStyle(.orange)
                }
            }

            Section {
                ForEach(Self.modules) { m in
                    HStack {
                        Circle()
                            .fill(color(for: results[m.name]))
                            .frame(width: 10, height: 10)
                        Text(m.name)
                        Spacer()
                        Text(caption(for: results[m.name]))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            } header: {
                Text("Modules")
            } footer: {
                if let checkedAt {
                    Text("Checked \(checkedAt.formatted(date: .omitted, time: .shortened)) · \(AppConfig.host)")
                }
            }

            if let voice {
                Section("San's voice") {
                    LabeledContent("Speech to text", value: voice.sttReady ? "Ready" : "Off")
                    LabeledContent("Text to speech", value: voice.ttsReady ? "Ready" : "Off")
                }
            }
        }
        .navigationTitle("Server")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button { Task { await check() } } label: {
                    Image(systemName: "arrow.clockwise")
                }
                .disabled(checking)
            }
        }
        .refreshable { await check() }
        .task { await check() }
    }

    private var allDown: Bool {
        results.count == Self.modules.count && results.values.allSatisfy { !$0.ok }
    }

    private func color(for r: ProbeResult?) -> Color {
        guard let r else { return .gray }
        return r.ok ? .green : .red
    }

    private func caption(for r: ProbeResult?) -> String {
        guard let r else { return checking ? "checking…" : "—" }
        return r.ok ? "\(r.ms) ms" : "down"
    }

    // All at once, so the screen takes as long as the slowest module rather than the sum.
    private func check() async {
        checking = true
        defer { checking = false }
        results = [:]

        await withTaskGroup(of: (String, ProbeResult).self) { group in
            for m in Self.modules {
                group.addTask { (m.name, await client.probe(m.port, m.path)) }
            }
            for await (name, result) in group {
                results[name] = result
            }
        }
        voice = try? await client.voiceStatus()
        checkedAt = .now
    }
}
