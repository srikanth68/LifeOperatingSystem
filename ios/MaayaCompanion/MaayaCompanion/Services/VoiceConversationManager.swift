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
    private(set) var inputDb: Float = -160      // live mic power, shown while listening
    private(set) var roomDb: Float = -160       // the room's measured quiet, ditto
    var isMuted = false

    private var isActive = false
    private var endTurnRequested = false

    // ── VAD / endpointing tuning (dBFS; AVAudioRecorder power runs −160…0) ──
    //
    // Thresholds are RELATIVE to the room, not fixed. The fixed −28 dB this replaced
    // never fired: the .voiceChat session applies noise suppression and gain control,
    // which holds ordinary speech around −45…−35 dB. The call sat on "Listening…",
    // discarded each 25-second clip as silence, and listened again -- forever.
    private let calibrationWindow: TimeInterval = 0.4 // measure the room before judging speech
    private let speechMargin: Float = 12         // this far above the room = speech
    private let silenceMargin: Float = 6         // within this of the room = silence
    private let minVoicedFrames = 3              // ~0.25s: a click is not a sentence
    // Trailing silence that ends a turn. 0.7s is still longer than a between-words pause,
    // and every tenth of a second here is added to every single reply.
    private let silenceHang: TimeInterval = 0.7
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
        // Warm the transcriber while the user is still drawing breath. The first audio
        // clip a call sends has been measured at 13s to transcribe against 1.7s for the
        // next one -- the model's audio path starting cold. Half a second of silence pays
        // that cost before anyone is waiting on it. The result is thrown away.
        Task { [client] in _ = try? await client.transcribe(Self.silentWav(seconds: 0.5)) }
        await runLoop()
    }

    // A valid 16 kHz mono 16-bit WAV of silence, built by hand so warming up needs no
    // bundled file.
    static func silentWav(seconds: Double, sampleRate: Int = 16_000) -> Data {
        let dataBytes = Int(Double(sampleRate) * seconds) * 2
        var d = Data()
        func tag(_ s: String) { d.append(contentsOf: Array(s.utf8)) }
        func u32(_ v: Int) { withUnsafeBytes(of: UInt32(v).littleEndian) { d.append(contentsOf: $0) } }
        func u16(_ v: Int) { withUnsafeBytes(of: UInt16(v).littleEndian) { d.append(contentsOf: $0) } }

        tag("RIFF"); u32(36 + dataBytes); tag("WAVE")
        tag("fmt "); u32(16); u16(1); u16(1); u32(sampleRate); u32(sampleRate * 2); u16(2); u16(16)
        tag("data"); u32(dataBytes)
        d.append(Data(count: dataBytes))
        return d
    }

    func stop() {
        isActive = false
        recorder?.stop(); recorder = nil
        player?.stop(); player = nil
        deactivateSession()
        level = 0
        phase = .idle
    }

    // Tap while listening: "I'm done, send it." The guaranteed way out when the room is
    // too loud for end-of-speech detection to tell voice from background.
    func finishTurn() {
        guard phase == .listening else { return }
        endTurnRequested = true
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
        var voicedFrames = 0
        var lastVoiceAt = Date()
        var floor: Float = 0            // the room's quiet; min() of real samples replaces 0
        var calibrated = false
        var forced = false
        endTurnRequested = false

        while isActive && !isMuted {
            try? await Task.sleep(nanoseconds: UInt64(meterInterval * 1_000_000_000))
            guard let r = recorder, r.isRecording else { break }
            r.updateMeters()
            let power = r.averagePower(forChannel: 0)
            inputDb = power
            let now = Date()

            if endTurnRequested { forced = true; break }

            // The quietest moment of the first 0.4s is the room. min() rather than an
            // average, so someone who starts talking straight away doesn't set it.
            if !calibrated {
                floor = min(floor, power)
                roomDb = floor
                level = normalizedLevel(power, floor: levelFloorDb)
                if now.timeIntervalSince(started) >= calibrationWindow { calibrated = true }
                continue
            }

            level = normalizedLevel(power, floor: floor)
            let speechOn = floor + speechMargin
            let silenceOff = floor + silenceMargin

            if power > speechOn {
                voicedFrames += 1
                if voicedFrames >= minVoicedFrames { hasSpoken = true }
                lastVoiceAt = now
            } else {
                if !hasSpoken { voicedFrames = 0 }
                // Follow a room that gets quieter at once, and one that gets louder slowly
                // -- a fan switching on shouldn't take a whole turn to be learned.
                if power < silenceOff {
                    floor = power < floor ? power : floor + (power - floor) * 0.05
                    roomDb = floor
                }
            }
            // End of turn: we heard speech, and it's been quiet for silenceHang.
            if hasSpoken && power < silenceOff && now.timeIntervalSince(lastVoiceAt) > silenceHang {
                break
            }
            if now.timeIntervalSince(started) > maxUtterance { break }
        }

        recorder?.stop()
        recorder = nil
        level = 0
        endTurnRequested = false

        defer { try? FileManager.default.removeItem(at: url) }
        // A tap to send is the user saying they spoke, whatever the meter thought.
        guard hasSpoken || forced, isActive else { return nil }

        let data = await finalisedRecording(at: url)
        // A 16 kHz mono 16-bit WAV runs 32 KB/s, so a kilobyte is about 30ms -- less
        // than a syllable. Sending it wastes a round trip and comes back as a decode
        // error, which reads like a bug rather than "you did not say anything".
        guard let data, data.count > 1024 else { return nil }
        return Self.repairWavHeader(data)
    }

    // AVAudioRecorder writes the RIFF and data-chunk sizes only when it finalises the file,
    // and that rewrite leaves the file's length unchanged -- so "the size stopped changing"
    // cannot tell a finished WAV from one still declaring zero bytes of audio. ffmpeg on
    // San trusts the declared size, decodes nothing, and the call fails with "That
    // recording couldn't be decoded" even though every sample is in the file.
    //
    // The samples are all there, so correct the header rather than trying to outwait it.
    // A header that is already right is left exactly as it was.
    static func repairWavHeader(_ data: Data) -> Data {
        var d = Data(data)                       // zero-based indices from here on
        guard d.count > 12,
              d.prefix(4) == Data("RIFF".utf8),
              d.subdata(in: 8..<12) == Data("WAVE".utf8) else { return data }

        var offset = 12
        while offset + 8 <= d.count {
            let id = d.subdata(in: offset..<(offset + 4))
            let size = Int(readUInt32LE(d, at: offset + 4))
            let body = offset + 8

            if id == Data("data".utf8) {
                let actual = d.count - body
                if size == 0 || size > actual {
                    writeUInt32LE(&d, UInt32(actual), at: offset + 4)
                }
                writeUInt32LE(&d, UInt32(d.count - 8), at: 4)
                return d
            }
            // Chunks are word-aligned: an odd-sized chunk is followed by one pad byte.
            offset = body + size + (size & 1)
        }
        return data
    }

    private static func readUInt32LE(_ d: Data, at i: Int) -> UInt32 {
        UInt32(d[i]) | UInt32(d[i + 1]) << 8 | UInt32(d[i + 2]) << 16 | UInt32(d[i + 3]) << 24
    }

    private static func writeUInt32LE(_ d: inout Data, _ v: UInt32, at i: Int) {
        d[i]     = UInt8(truncatingIfNeeded: v)
        d[i + 1] = UInt8(truncatingIfNeeded: v >> 8)
        d[i + 2] = UInt8(truncatingIfNeeded: v >> 16)
        d[i + 3] = UInt8(truncatingIfNeeded: v >> 24)
    }

    // AVAudioRecorder.stop() closes the file on its own queue, and the RIFF header's
    // length fields are written during that finalisation. Reading the moment stop()
    // returns can therefore capture a WAV that declares zero bytes of audio -- which
    // the server's ffmpeg rejects as "that recording couldn't be decoded". The file is
    // there; it just isn't finished.
    //
    // Waiting for the size to settle rather than using the delegate keeps this class as
    // it is: AVAudioRecorderDelegate is an NSObject protocol, and conforming would mean
    // restructuring an @Observable @MainActor type for one callback. In practice this
    // settles on the first or second pass.
    private func finalisedRecording(at url: URL) async -> Data? {
        var previous = -1
        for _ in 0..<40 {                      // 2s ceiling; never reached in practice
            let size = fileSize(url)
            if size > 44, size == previous { break }
            previous = size
            try? await Task.sleep(nanoseconds: 50_000_000)
        }
        return try? Data(contentsOf: url)
    }

    private func fileSize(_ url: URL) -> Int {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              let size = attrs[.size] as? Int else { return 0 }
        return size
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

    // Orb size from how far above the room the mic is, over a 30 dB span -- so it moves
    // with your voice even when voice-chat processing keeps absolute levels low.
    private func normalizedLevel(_ db: Float, floor: Float) -> Double {
        Double(min(max((db - floor) / 30, 0), 1))
    }
}
