import Foundation

// Authenticated read-only client for the module dashboards and the San chat.
// Every request carries `Authorization: Bearer <token>`; on a 401 it attempts a
// single silent refresh and retries once, then surfaces .sessionExpired so the
// UI can drop back to the login screen — the same contract as the web app's
// session-expiry interceptor.
final class MaayaClient {
    private let auth: AuthService

    init(auth: AuthService) {
        self.auth = auth
    }

    // MARK: - Dashboard reads

    func vaultSummary() async throws -> VaultSummary {
        try await get(ModulePort.vault, "/api/summary")
    }
    func vitaraDashboard() async throws -> VitaraDashboard {
        try await get(ModulePort.vitara, "/api/dashboard")
    }
    func aasthiSummary() async throws -> AasthiSummary {
        try await get(ModulePort.aasthi, "/api/properties/summary")
    }
    func sutraStats() async throws -> SutraStats {
        try await get(ModulePort.sutra, "/api/documents/stats")
    }
    func karmaToday() async throws -> [KarmaHabit] {
        try await get(ModulePort.karma, "/api/habits/today")
    }
    func northStarDashboard() async throws -> NorthStarDashboard {
        try await get(ModulePort.northstar, "/api/dashboard")
    }
    func nexusStatus() async throws -> NexusStatus {
        try await get(ModulePort.nexus, "/api/nexus/sentinel/status")
    }
    func nexusBoard() async throws -> [NexusBoardRow] {
        try await get(ModulePort.nexus, "/api/nexus/sentinel/board")
    }

    // MARK: - Reminders & alerts (for local-notification scheduling)

    func reminders() async throws -> [ReminderItem] {
        try await get(ModulePort.san, "/api/reminders")
    }
    func alerts() async throws -> [AlertItem] {
        try await get(ModulePort.san, "/api/alerts")
    }

    // MARK: - San chat

    func chatHistory() async throws -> [ChatMessage] {
        try await get(ModulePort.san, "/api/chat/messages")
    }
    func sendChat(_ content: String) async throws -> ChatSendResult {
        try await send(ModulePort.san, "/api/chat/messages", method: "POST", body: ChatSendBody(content: content))
    }

    // MARK: - San voice (Whisper STT + Piper TTS proxy, local-only)

    func voiceStatus() async throws -> VoiceStatus {
        try await get(ModulePort.san, "/api/voice/status")
    }

    // Speech → text. Uploads the recorded clip as multipart form field "audio"
    // (the name the server's IFormFile parameter binds to).
    func transcribe(_ audio: Data, filename: String = "utterance.wav", mime: String = "audio/wav") async throws -> String {
        let boundary = "Boundary-\(UUID().uuidString)"
        var body = Data()
        func appendString(_ s: String) { body.append(s.data(using: .utf8)!) }
        appendString("--\(boundary)\r\n")
        appendString("Content-Disposition: form-data; name=\"audio\"; filename=\"\(filename)\"\r\n")
        appendString("Content-Type: \(mime)\r\n\r\n")
        body.append(audio)
        appendString("\r\n--\(boundary)--\r\n")

        let data = try await performData(ModulePort.san, "/api/voice/transcribe", method: "POST",
                                         body: body, contentType: "multipart/form-data; boundary=\(boundary)")
        return try MaayaJSON.decoder.decode(VoiceTranscribeResult.self, from: data).text
    }

    // Text → speech. Returns the raw audio bytes (mp3) for AVAudioPlayer.
    func speak(_ text: String, voice: String? = nil) async throws -> Data {
        let bodyData = try MaayaJSON.encoder.encode(VoiceSpeakBody(text: text, voice: voice))
        return try await performData(ModulePort.san, "/api/voice/speak", method: "POST",
                                     body: bodyData, contentType: "application/json")
    }

    // MARK: - Core request plumbing

    func get<T: Decodable>(_ port: Int, _ path: String) async throws -> T {
        try await request(port, path, method: "GET", httpBody: nil)
    }

    private func send<B: Encodable, T: Decodable>(_ port: Int, _ path: String, method: String, body: B) async throws -> T {
        let httpBody = try MaayaJSON.encoder.encode(body)
        return try await request(port, path, method: method, httpBody: httpBody)
    }

    private func request<T: Decodable>(_ port: Int, _ path: String, method: String, httpBody: Data?) async throws -> T {
        let data = try await perform(port, path, method: method, httpBody: httpBody, allowRefresh: true)
        return try MaayaJSON.decoder.decode(T.self, from: data)
    }

    // Generalized variant: arbitrary Content-Type (multipart, etc.) and returns
    // the raw bytes without JSON decoding — used by the voice endpoints, where
    // the request is multipart and the /speak response is audio. Shares the same
    // Bearer + single-silent-refresh contract as `perform`.
    private func performData(_ port: Int, _ path: String, method: String, body: Data?, contentType: String?, allowRefresh: Bool = true) async throws -> Data {
        let urlString = "\(AppConfig.moduleURL(port))\(path)"
        guard let url = URL(string: urlString) else { throw APIError.invalidURL(urlString) }

        var req = URLRequest(url: url)
        req.httpMethod = method
        req.timeoutInterval = 60   // Whisper/Piper can be slower than a JSON read
        if let token = auth.accessToken {
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        if let body {
            req.httpBody = body
            if let contentType { req.setValue(contentType, forHTTPHeaderField: "Content-Type") }
        }

        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw APIError.invalidResponse }

        if http.statusCode == 401 {
            if allowRefresh, await auth.refresh() {
                return try await performData(port, path, method: method, body: body, contentType: contentType, allowRefresh: false)
            }
            auth.forceLogout()
            throw APIError.sessionExpired
        }

        guard (200...299).contains(http.statusCode) else {
            let body = String(data: data, encoding: .utf8) ?? "No response body"
            throw APIError.serverError(statusCode: http.statusCode, message: body)
        }
        return data
    }

    private func perform(_ port: Int, _ path: String, method: String, httpBody: Data?, allowRefresh: Bool) async throws -> Data {
        let urlString = "\(AppConfig.moduleURL(port))\(path)"
        guard let url = URL(string: urlString) else { throw APIError.invalidURL(urlString) }

        var req = URLRequest(url: url)
        req.httpMethod = method
        req.timeoutInterval = 30
        if let token = auth.accessToken {
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        if let httpBody {
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = httpBody
        }

        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw APIError.invalidResponse }

        if http.statusCode == 401 {
            if allowRefresh, await auth.refresh() {
                return try await perform(port, path, method: method, httpBody: httpBody, allowRefresh: false)
            }
            auth.forceLogout()
            throw APIError.sessionExpired
        }

        guard (200...299).contains(http.statusCode) else {
            let body = String(data: data, encoding: .utf8) ?? "No response body"
            throw APIError.serverError(statusCode: http.statusCode, message: body)
        }
        return data
    }
}
