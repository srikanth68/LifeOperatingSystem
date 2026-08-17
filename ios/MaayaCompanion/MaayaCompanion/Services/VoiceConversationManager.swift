import Foundation
import AVFoundation
import Observation

// Continuous, hands-free voice conversation with San — "call mode".
//
// The loop: listen → (auto-detect you stopped talking via mic silence) →
// transcribe on the local Whisper → send to San chat → speak the reply on the
// local Piper → listen again. No push-to-talk, no send button. Everything stays
// on the mesh (Whisper + Piper + Gemma all run on Everest); nothing hits a cloud
// speech API.
//
// Endpointing is done locally from AVAudioRecorder metering (a spike marks the
// start of speech, a trailing run of silence marks the end) so we only ship a
// complete utterance to Whisper. Barge-in is tap-to-interrupt: tap while San is
// talking to cut the reply short and start listening immediately — reliable
// without echo-cancellation gymnastics (see the note on runLoop).
@MainActor
@Observable
final class VoiceConversationManager {
    enum Phase: Equatable {
        case idle, listening, transcribing, thinking, speaking, muted
        case error(String)
    }

    private let client: MaayaClient

    private(set) var phase: Phase = .idle
    private(set) var level: Double = 0          // 0…1 mic input, drives the orb
    private(set) var lastUserText = ""
    private(set) var lastSanText = ""
    private(set) var micDenied = false
    var isMuted = false

    private var isActive = false

    // ── VAD / endpointing tuning (dBFS; AVAudioRecorder power runs −160…0) ──
    private let speechOnThreshold: Float = -28   // above this = speech present
    private let silenceOffThreshold: Float = -37 // below this = silence
    private let silenceHang: TimeInterval = 1.0  // trailing silence that ends a turn
    private let maxUtterance: TimeInterval = 25  // hard cap on one turn
    private let meterInterval: TimeInterval = 0.08
    private let levelFloorDb: Float = -50        // maps to level 0 on the orb

    private var recorder: AVAudioRecorder?
    private var player: AVAudioPlayer?

    init(client: MaayaClient) { self.client = client }

    // MARK: - Lifecycle

    func start() async {
        guard await ensurePermission() else {
            micDenied = true
            phase = .error("Microphone access is off. Enable it in Settings › MaayaCompanion.")
            return
        }
        configureSession()
        isActive = true
        await runLoop()
    }

    func stop() {
        isActive = false
        recorder?.stop(); recorder = nil
        player?.stop(); player = nil
        deactivateSession()
        level = 0
        phase = .idle
    }

    // Tap-to-interrupt San mid-reply → straight back to listening.
    func interrupt() {
        guard phase == .speaking else { return }
        player?.stop(); player = nil
    }

    func toggleMute() {
        isMuted.toggle()
        if isMuted {
            recorder?.stop(); recorder = nil   // drop any in-progress capture
            player?.stop(); player = nil
            level = 0
            phase = .muted
        }
    }

    // MARK: - The conversation loop

    private func runLoop() async {
        while isActive {
            // Honour mute without spinning the mic.
            while isMuted && isActive {
                phase = .muted
                try? await Task.sleep(nanoseconds: 200_000_000)
            }
            guard isActive else { return }

            do {
                phase = .listening
                guard let audio = await captureUtterance(), isActive else { continue }

                phase = .transcribing
                let heard = try await client.transcribe(audio)
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                guard isActive else { return }
                if heard.isEmpty { continue }   // Whisper heard nothing usable → keep listening
                lastUserText = heard

                phase = .thinking
                // mode: "voice" is what puts the server on its spoken path -- 8 tools
                // instead of 40, the speak-aloud output rules, a smaller history window
                // and its own cache slot. Without it San answers a phone call in
                // markdown, formatted for a screen nobody is looking at.
                let reply = try await client.sendChat(heard, mode: "voice").assistantMessage.content
                guard isActive else { return }
                lastSanText = reply

                phase = .speaking
                try await speakInChunks(reply)
                guard isActive else { return }
            } catch APIError.sessionExpired {
                phase = .error("Session expired — reopen after signing in again.")
                isActive = false
                return
            } catch {
                // A transient failure (network blip, service down) shouldn't kill
                // the call — surface it briefly, then resume listening.
                phase = .error(error.localizedDescription)
                try? await Task.sleep(nanoseconds: 1_500_000_000)
            }
        }
    }

    // Speaks a reply piece by piece, synthesising the next piece while the current one
    // plays. The first chunk is short on purpose: it is the only wait the listener
    // actually experiences. See SpeechChunks.
    private func speakInChunks(_ reply: String) async throws {
        let chunks = SpeechChunks.split(reply)
        guard !chunks.isEmpty else { return }

        // One chunk ahead, no more. Two would not arrive sooner -- the server renders
        // them one at a time anyway -- and would waste work whenever the call is ended
        // or barged in on mid-reply.
        var next: Task<Data, Error>? = Task { [client] in try await client.speak(chunks[0]) }

        for i in chunks.indices {
            guard isActive else { next?.cancel(); return }
            guard let current = next else { return }

            if i + 1 < chunks.count {
                let following = chunks[i + 1]
                next = Task { [client] in try await client.speak(following) }
            } else {
                next = nil
            }

            let audio = try await current.value
            guard isActive else { next?.cancel(); return }
            await play(audio)
        }
    }

    // MARK: - Capture with local endpointing

    // Records until the user finishes a sentence (trailing silence) or the cap is
    // hit. Returns the clip, or nil if nothing was spoken / the call was ended.
    private func captureUtterance() async -> Data? {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("utterance-\(UUID().uuidString).wav")
        let settings: [String: Any] = [
            AVFormatIDKey: Int(kAudioFormatLinearPCM),
            AVSampleRateKey: 16_000,          // Whisper's native rate — no server-side resample
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
        ]

        guard let rec = try? AVAudioRecorder(url: url, settings: settings) else { return nil }
        rec.isMeteringEnabled = true
        recorder = rec
        guard rec.record() else { recorder = nil; return nil }

        let started = Date()
        var hasSpoken = false
        var lastVoiceAt = Date()

        while isActive && !isMuted {
            try? await Task.sleep(nanoseconds: UInt64(meterInterval * 1_000_000_000))
            guard let r = recorder, r.isRecording else { break }
            r.updateMeters()
            let power = r.averagePower(forChannel: 0)
            level = normalizedLevel(power)
            let now = Date()

            if power > speechOnThreshold {
                hasSpoken = true
                lastVoiceAt = now
            }
            // End of turn: we heard speech, and it's been quiet for silenceHang.
            if hasSpoken && power < silenceOffThreshold && now.timeIntervalSince(lastVoiceAt) > silenceHang {
                break
            }
            if now.timeIntervalSince(started) > maxUtterance { break }
        }

        recorder?.stop()
        recorder = nil
        level = 0

        defer { try? FileManager.default.removeItem(at: url) }
        guard hasSpoken, isActive else { return nil }
        return try? Data(contentsOf: url)
    }

    // MARK: - Playback

    private func play(_ data: Data) async {
        guard let p = try? AVAudioPlayer(data: data) else { return }
        player = p
        p.prepareToPlay()
        p.play()
        // Poll rather than use a delegate so tap-to-interrupt (which nils `player`)
        // and ending the call both break out cleanly.
        while let cur = player, cur.isPlaying, isActive {
            try? await Task.sleep(nanoseconds: 100_000_000)
        }
        player?.stop()
        player = nil
    }

    // MARK: - Audio session & permission

    private func configureSession() {
        let s = AVAudioSession.sharedInstance()
        // .voiceChat enables the system's echo/noise processing on the input, and
        // .defaultToSpeaker keeps San audible hands-free.
        try? s.setCategory(.playAndRecord, mode: .voiceChat,
                           options: [.defaultToSpeaker, .allowBluetooth, .duckOthers])
        try? s.setActive(true)
    }

    private func deactivateSession() {
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    private func ensurePermission() async -> Bool {
        await withCheckedContinuation { cont in
            switch AVAudioApplication.shared.recordPermission {
            case .granted: cont.resume(returning: true)
            case .denied:  cont.resume(returning: false)
            default:       AVAudioApplication.requestRecordPermission { cont.resume(returning: $0) }
            }
        }
    }

    private func normalizedLevel(_ db: Float) -> Double {
        guard db > levelFloorDb else { return 0 }
        return Double((db - levelFloorDb) / -levelFloorDb)   // (db + 50) / 50
    }
}
