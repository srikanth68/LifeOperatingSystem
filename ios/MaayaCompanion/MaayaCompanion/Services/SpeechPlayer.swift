import Foundation
import AVFoundation
import Observation

// Lightweight one-shot TTS playback for the text chat's "read replies aloud"
// toggle. (Call mode uses VoiceConversationManager's own player instead.)
@MainActor
@Observable
final class SpeechPlayer {
    private let client: MaayaClient
    private var player: AVAudioPlayer?
    private(set) var isSpeaking = false

    init(client: MaayaClient) { self.client = client }

    func speak(_ text: String) async {
        stop()
        guard let data = try? await client.speak(text) else { return }
        try? AVAudioSession.sharedInstance().setCategory(.playback, options: [.duckOthers])
        try? AVAudioSession.sharedInstance().setActive(true)
        guard let p = try? AVAudioPlayer(data: data) else { return }
        player = p
        p.play()
        isSpeaking = true
        while let cur = player, cur.isPlaying {
            try? await Task.sleep(nanoseconds: 150_000_000)
        }
        isSpeaking = false
        player = nil
    }

    func stop() {
        player?.stop()
        player = nil
        isSpeaking = false
    }
}
