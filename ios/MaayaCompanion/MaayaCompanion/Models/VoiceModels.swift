import Foundation

// San's voice proxy (port 5300, api/voice) — self-hosted Whisper (STT) + Piper
// (TTS), fully local. Mirrors the web app's services/voice.ts contract.

// GET /api/voice/status — controls whether the mic/call buttons appear.
struct VoiceStatus: Codable {
    let sttReady: Bool
    let ttsReady: Bool
    var callReady: Bool { sttReady && ttsReady }
}

// POST /api/voice/transcribe (multipart) → { text }
struct VoiceTranscribeResult: Codable {
    let text: String
}

// POST /api/voice/speak → audio bytes. Voice is optional; nil lets the server
// use its configured PIPER_VOICE default (e.g. "amy").
struct VoiceSpeakBody: Codable {
    let text: String
    let voice: String?
}
