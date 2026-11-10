import SwiftUI

// Say a thought, read it back, save it to NorthStar.
//
// Speech goes to San's transcriber on Everest -- over the mesh, never a cloud speech
// API -- and the text comes back for editing before anything is stored, because a
// misheard name saved as memory gets recalled into San's answers until someone deletes
// it.
//
// Saved as ACTIVE, not proposed. The review queue exists for what an agent concluded on
// its own; this is the owner's own words, checked by the owner, so asking them to
// confirm it a second time would be noise. Source "phone-voice" keeps it traceable.
struct VoiceNoteView: View {
    let client: MaayaClient
    let onSaved: () -> Void

    private enum Kind: String, CaseIterable, Identifiable {
        case observation, fact, decision, event, preference
        var id: String { rawValue }
        var label: String { self == .observation ? "Note" : rawValue.capitalized }
    }

    private enum Phase: Equatable {
        case ready, recording, transcribing(part: Int, of: Int), editing, saving, saved(String)
    }

    @Environment(\.dismiss) private var dismiss
    @State private var recorder = VoiceNoteRecorder()
    @State private var phase: Phase = .ready
    @State private var text = ""
    @State private var kind: Kind = .observation
    @State private var pendingAudio: Data?      // kept when transcription fails, for a retry
    @State private var errorText: String?

    var body: some View {
        NavigationStack {
            Group {
                switch phase {
                case .ready, .recording:
                    recordPane
                case .transcribing(let part, let of):
                    VStack(spacing: 12) {
                        ProgressView()
                        Text(of > 1 ? "Transcribing part \(part) of \(of)…" : "Transcribing…")
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                case .editing, .saving:
                    editPane
                case .saved(let message):
                    VStack(spacing: 12) {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 56))
                            .foregroundStyle(.green)
                        Text(message)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .safeAreaInset(edge: .top) { errorBanner }
            .navigationTitle("Voice note")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { recorder.cancel(); dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    if phase == .editing || phase == .saving {
                        Button("Save") { Task { await save() } }
                            .disabled(phase == .saving || trimmed.isEmpty)
                    }
                }
            }
            // The cap stops the recorder by itself; carry on as if Stop was tapped.
            .onChange(of: recorder.isRecording) { _, recording in
                if !recording && phase == .recording { Task { await finish() } }
            }
            .onDisappear { recorder.cancel() }
        }
        // A swipe down should not throw away something already said.
        .interactiveDismissDisabled(phase == .recording || !text.isEmpty || pendingAudio != nil)
    }

    // MARK: - Panes

    private var recordPane: some View {
        VStack(spacing: 24) {
            Spacer()
            Button {
                Task {
                    if phase == .recording { await finish() } else { await startRecording() }
                }
            } label: {
                ZStack {
                    Circle()
                        .fill(phase == .recording ? Color.red : MaayaTheme.gold)
                        .frame(width: 120, height: 120)
                        .scaleEffect(phase == .recording ? 1 + recorder.level * 0.2 : 1)
                        .animation(.easeOut(duration: 0.1), value: recorder.level)
                    Image(systemName: phase == .recording ? "stop.fill" : "mic.fill")
                        .font(.system(size: 44))
                        .foregroundStyle(.black)
                }
            }
            .buttonStyle(.plain)
            .accessibilityLabel(phase == .recording ? "Stop recording" : "Start recording")

            Text(prompt)
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .monospacedDigit()

            if pendingAudio != nil {
                Button("Transcribe again") { Task { await retry() } }
            }
            Spacer()
        }
        .padding()
    }

    private var editPane: some View {
        Form {
            Section {
                TextEditor(text: $text)
                    .frame(minHeight: 160)
            } header: {
                Text("What you said")
            } footer: {
                Text("Fix anything misheard before saving. It is stored exactly as written here.")
            }

            Section("Save as") {
                Picker("Kind", selection: $kind) {
                    ForEach(Kind.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.segmented)
            }

            Section {
                Button { Task { await startRecording() } } label: {
                    Label("Keep talking", systemImage: "mic")
                }
                if pendingAudio != nil {
                    Button("Transcribe the last part again") { Task { await retry() } }
                }
            }
        }
        .disabled(phase == .saving)
    }

    @ViewBuilder
    private var errorBanner: some View {
        if let errorText {
            Label(errorText, systemImage: "exclamationmark.triangle")
                .font(.callout)
                .foregroundStyle(.orange)
                .padding(.horizontal)
                .padding(.vertical, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(.bar)
        }
    }

    private var prompt: String {
        if phase == .recording {
            return "\(Self.clock(recorder.elapsed)) of \(Self.clock(VoiceNoteRecorder.maxDuration)) · tap to stop"
        }
        return text.isEmpty ? "Tap to record. Up to two minutes." : "Tap to add more."
    }

    private var trimmed: String { text.trimmingCharacters(in: .whitespacesAndNewlines) }

    private static func clock(_ t: TimeInterval) -> String {
        String(format: "%d:%02d", Int(t) / 60, Int(t) % 60)
    }

    // MARK: - Steps

    private func startRecording() async {
        errorText = nil
        if let problem = await recorder.start() {
            errorText = problem
            return
        }
        phase = .recording
    }

    private func finish() async {
        // Leave .recording first, so the onChange the stop triggers does not finish twice.
        phase = .transcribing(part: 1, of: 1)
        guard let audio = await recorder.stop() else {
            errorText = "Nothing was recorded."
            phase = text.isEmpty ? .ready : .editing
            return
        }
        await transcribe(audio)
    }

    private func retry() async {
        guard let audio = pendingAudio else { return }
        errorText = nil
        await transcribe(audio)
    }

    // Pieces in order, one at a time: they share one transcriber slot on Everest anyway,
    // and the text has to come back in the order it was said.
    private func transcribe(_ audio: Data) async {
        let parts = VoiceNoteRecorder.chunks(audio)
        var heard: [String] = []
        for (i, part) in parts.enumerated() {
            phase = .transcribing(part: i + 1, of: parts.count)
            do {
                let t = try await client.transcribe(part, filename: "note.wav")
                heard.append(t.trimmingCharacters(in: .whitespacesAndNewlines))
            } catch {
                pendingAudio = audio
                errorText = "Couldn't transcribe: \(error.localizedDescription). The recording is kept."
                phase = text.isEmpty ? .ready : .editing
                return
            }
        }
        pendingAudio = nil

        let said = heard.filter { !$0.isEmpty }.joined(separator: " ")
        if said.isEmpty {
            errorText = "Couldn't make out any words."
        } else {
            errorText = nil
            text = text.isEmpty ? said : text + " " + said
        }
        phase = text.isEmpty ? .ready : .editing
    }

    private func save() async {
        let content = trimmed
        guard !content.isEmpty else { return }
        phase = .saving
        do {
            let result = try await client.saveMemory(content, kind: kind.rawValue)
            phase = .saved(result.note.map { "Saved — \($0)." } ?? "Saved to memory.")
            onSaved()
            try? await Task.sleep(nanoseconds: 1_200_000_000)
            dismiss()
        } catch {
            errorText = error.localizedDescription
            phase = .editing
        }
    }
}
