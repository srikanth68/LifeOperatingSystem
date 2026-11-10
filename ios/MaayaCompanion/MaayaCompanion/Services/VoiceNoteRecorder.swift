import Foundation
import AVFoundation
import Observation

// Records one voice note: tap to start, tap to stop, two minutes at most.
//
// Unlike call mode there is no listening for silence -- a thought said aloud has pauses
// in it, and ending the note at the first one would cut it in half. The person decides
// when it is finished.
//
// San's transcriber is Gemma hearing the audio, and one clip is kept under half a minute
// (call mode caps a turn at 25s for the same reason). A two-minute note is therefore cut
// into pieces before it is sent, each cut placed at the quietest moment near the limit
// so it falls between words rather than through one.
@Observable
final class VoiceNoteRecorder {
    static let maxDuration: TimeInterval = 120

    private(set) var isRecording = false
    private(set) var elapsed: TimeInterval = 0
    private(set) var level: Double = 0          // 0…1, drives the button's pulse

    private var recorder: AVAudioRecorder?
    private var fileURL: URL?
    private var meterTask: Task<Void, Never>?

    // Returns an error to show, or nil once recording has started.
    func start() async -> String? {
        guard await Self.permission() else {
            return "Microphone access is off. Enable it in Settings › MaayaCompanion."
        }
        let session = AVAudioSession.sharedInstance()
        do {
            try session.setCategory(.playAndRecord, mode: .default,
                                    options: [.defaultToSpeaker, .allowBluetooth])
            try session.setActive(true)
        } catch {
            return "Couldn't start the microphone: \(error.localizedDescription)"
        }

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("note-\(UUID().uuidString).wav")
        let settings: [String: Any] = [
            AVFormatIDKey: Int(kAudioFormatLinearPCM),
            AVSampleRateKey: 16_000,            // what the transcriber wants, no resample
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
        ]
        guard let rec = try? AVAudioRecorder(url: url, settings: settings) else {
            return "Couldn't start recording."
        }
        rec.isMeteringEnabled = true
        // record(forDuration:) stops by itself at the cap; the meter loop notices.
        guard rec.record(forDuration: Self.maxDuration) else { return "Couldn't start recording." }

        recorder = rec
        fileURL = url
        elapsed = 0
        isRecording = true

        meterTask = Task { [weak self] in
            while let me = self, let r = me.recorder, r.isRecording {
                r.updateMeters()
                me.level = Double(min(max((r.averagePower(forChannel: 0) + 50) / 50, 0), 1))
                me.elapsed = r.currentTime
                try? await Task.sleep(nanoseconds: 100_000_000)
            }
            self?.level = 0
            self?.isRecording = false
        }
        return nil
    }

    // Stops (if the cap has not already) and hands back the finished WAV, or nil if
    // there was nothing worth sending.
    func stop() async -> Data? {
        let url = fileURL
        halt()
        guard let url else { return nil }
        defer { try? FileManager.default.removeItem(at: url) }

        let data = await Self.settled(url)
        guard let data, data.count > 1024 else { return nil }   // ~30ms: nothing said
        return VoiceConversationManager.repairWavHeader(data)
    }

    // Leaving the sheet mid-recording: drop the audio.
    func cancel() {
        let url = fileURL
        halt()
        if let url { try? FileManager.default.removeItem(at: url) }
    }

    private func halt() {
        meterTask?.cancel()
        meterTask = nil
        recorder?.stop()
        recorder = nil
        fileURL = nil
        isRecording = false
        level = 0
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    // MARK: - Cutting a long note into transcribable pieces

    private static let bytesPerSecond = 32_000          // 16 kHz × mono × 16-bit
    private static let frameBytes = 1_600               // 50ms

    // Pieces of at most `maxSeconds`, plus up to 2s more rather than leaving a tail too
    // short to transcribe. Each cut sits at the quietest 50ms in the last 5s of its
    // window. A short note comes back as it went in.
    static func chunks(_ wav: Data, maxSeconds: Double = 25) -> [Data] {
        guard let pcm = pcmBody(wav) else { return [wav] }
        let window = Int(maxSeconds) * bytesPerSecond
        let slack = 2 * bytesPerSecond
        guard pcm.count > window + slack else { return [wav] }

        var pieces: [Data] = []
        var start = 0
        while start < pcm.count {
            let remaining = pcm.count - start
            if remaining <= window + slack {
                pieces.append(wavFile(pcm.subdata(in: start..<pcm.count)))
                break
            }
            let cut = quietestFrame(pcm, from: start + window - 5 * bytesPerSecond, to: start + window)
            pieces.append(wavFile(pcm.subdata(in: start..<cut)))
            start = cut
        }
        return pieces
    }

    // Start of the 50ms frame with the least energy in [from, to). Offsets stay even, so
    // a cut never splits a 16-bit sample.
    private static func quietestFrame(_ pcm: Data, from: Int, to: Int) -> Int {
        var best = to
        var bestEnergy = Int.max
        var offset = from - (from % 2)
        while offset + frameBytes <= to {
            var energy = 0
            var i = offset
            while i + 1 < offset + frameBytes {
                let sample = Int16(bitPattern: UInt16(pcm[i]) | UInt16(pcm[i + 1]) << 8)
                energy += abs(Int(sample))
                i += 2
            }
            if energy < bestEnergy { bestEnergy = energy; best = offset }
            offset += frameBytes
        }
        return best
    }

    // The samples of a (header-repaired) WAV, without its chunks, as a zero-based Data.
    private static func pcmBody(_ wav: Data) -> Data? {
        let d = Data(wav)
        guard d.count > 12, d.prefix(4) == Data("RIFF".utf8) else { return nil }
        var offset = 12
        while offset + 8 <= d.count {
            let id = d.subdata(in: offset..<(offset + 4))
            let size = Int(UInt32(d[offset + 4]) | UInt32(d[offset + 5]) << 8
                           | UInt32(d[offset + 6]) << 16 | UInt32(d[offset + 7]) << 24)
            let body = offset + 8
            if id == Data("data".utf8) {
                return Data(d.subdata(in: body..<min(body + size, d.count)))
            }
            offset = body + size + (size & 1)
        }
        return nil
    }

    // A 16 kHz mono 16-bit WAV around raw samples.
    private static func wavFile(_ pcm: Data) -> Data {
        var d = Data()
        func tag(_ s: String) { d.append(contentsOf: Array(s.utf8)) }
        func u32(_ v: Int) { withUnsafeBytes(of: UInt32(v).littleEndian) { d.append(contentsOf: $0) } }
        func u16(_ v: Int) { withUnsafeBytes(of: UInt16(v).littleEndian) { d.append(contentsOf: $0) } }

        tag("RIFF"); u32(36 + pcm.count); tag("WAVE")
        tag("fmt "); u32(16); u16(1); u16(1); u32(16_000); u32(bytesPerSecond); u16(2); u16(16)
        tag("data"); u32(pcm.count)
        d.append(pcm)
        return d
    }

    // MARK: - Plumbing

    // The recorder finishes writing the file on its own queue after stop(); wait for the
    // size to stop changing (same reason as call mode's finalisedRecording).
    private static func settled(_ url: URL) async -> Data? {
        var previous = -1
        for _ in 0..<40 {
            let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int) ?? 0
            if size > 44, size == previous { break }
            previous = size
            try? await Task.sleep(nanoseconds: 50_000_000)
        }
        return try? Data(contentsOf: url)
    }

    private static func permission() async -> Bool {
        await withCheckedContinuation { cont in
            switch AVAudioApplication.shared.recordPermission {
            case .granted: cont.resume(returning: true)
            case .denied:  cont.resume(returning: false)
            default:       AVAudioApplication.requestRecordPermission { cont.resume(returning: $0) }
            }
        }
    }
}
