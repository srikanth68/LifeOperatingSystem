import Foundation

// Authenticated read-only client for the module dashboards and the San chat.
// Every request carries `Authorization: Bearer <token>`; on a 401 it attempts a
// single silent refresh and retries once, then surfaces .sessionExpired so the
// UI can drop back to the login screen — the same contract as the web app's
// session-expiry interceptor.
final class MaayaClient {
    // A San chat turn runs a real agent loop against a local model: it prefills several
    // thousand tokens, may call tools, and has been measured between 6 and 50 seconds on
    // Everest. The old blanket 30s was shorter than San's ordinary reply, so sending
    // simply failed -- reading, which is fast, always worked, which is what made it look
    // like a send bug rather than a timeout.
    static let chatTimeout: TimeInterval = 180
    static let defaultTimeout: TimeInterval = 30
    static let voiceTimeout: TimeInterval = 120

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

    // MARK: - Actions (NorthStar queue) and completions

    func pendingActions(limit: Int = 50) async throws -> [ActionItem] {
        try await get(ModulePort.northstar, "/api/actions?status=pending&limit=\(limit)")
    }

    @discardableResult
    func completeAction(_ id: String) async throws -> Data {
        try await perform(ModulePort.northstar, "/api/actions/\(id)", method: "PATCH",
                          httpBody: try MaayaJSON.encoder.encode(
                              UpdateActionBody(status: "completed", resolvedBy: "ios")),
                          allowRefresh: true)
    }

    // The server binds a bare JSON boolean here, not an object.
    @discardableResult
    func setReminderDone(_ id: String, done: Bool = true) async throws -> Data {
        try await perform(ModulePort.san, "/api/reminders/\(id)/done", method: "PATCH",
                          httpBody: try MaayaJSON.encoder.encode(done),
                          allowRefresh: true)
    }

    @discardableResult
    func createReminder(text: String, dueAt: Date, notifyTelegram: Bool = true) async throws -> Data {
        try await perform(ModulePort.san, "/api/reminders", method: "POST",
                          httpBody: try MaayaJSON.encoder.encode(
                              CreateReminderBody(text: text, dueAt: dueAt, notifyTelegram: notifyTelegram)),
                          allowRefresh: true)
    }

    // Tick a Karma habit for today. Karma keys the log by habit GUID + date, so
    // sending the same day twice corrects rather than duplicates.
    @discardableResult
    func logHabit(_ id: String, completed: Bool = true) async throws -> Data {
        try await perform(ModulePort.karma, "/api/habits/\(id)/log", method: "POST",
                          httpBody: try MaayaJSON.encoder.encode(
                              HabitLogBody(date: nil, completed: completed, note: nil)),
                          allowRefresh: true)
    }

    // Push a reminder's due time out. The endpoint is a full replace, so the existing
    // text has to be sent back with it or it would be blanked.
    @discardableResult
    func snoozeReminder(_ r: ReminderItem, to newDue: Date) async throws -> Data {
        try await perform(ModulePort.san, "/api/reminders/\(r.id)", method: "PUT",
                          httpBody: try MaayaJSON.encoder.encode(
                              ReminderUpsertBody(text: r.text, dueAt: newDue,
                                                 notifyTelegram: r.notifyTelegram, done: false)),
                          allowRefresh: true)
    }

    // Dismissing an alert deletes it. There is no "seen" state server-side, and an
    // alert you have dealt with should stop occupying San's context block as well as
    // the app's list -- deleting achieves both, and the underlying obligation lives in
    // whichever module actually owns it.
    @discardableResult
    func dismissAlert(_ id: String) async throws -> Data {
        try await perform(ModulePort.san, "/api/alerts/\(id)", method: "DELETE",
                          httpBody: nil, allowRefresh: true)
    }

    // MARK: - San chat

    func chatHistory() async throws -> [ChatMessage] {
        try await get(ModulePort.san, "/api/chat/messages")
    }
    // mode: "voice" on a spoken turn, nil when typed. imageDataUrl carries an attached
    // photo as a data: URL. Both are optional on the wire, so an older server ignores them.
    func sendChat(_ content: String,
                  imageDataUrl: String? = nil,
                  mode: String? = nil) async throws -> ChatSendResult {
        try await send(ModulePort.san, "/api/chat/messages", method: "POST",
                       body: ChatSendBody(content: content, imageDataUrl: imageDataUrl, mode: mode),
                       timeout: Self.chatTimeout)
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

    private func send<B: Encodable, T: Decodable>(_ port: Int, _ path: String, method: String, body: B,
                                                  timeout: TimeInterval? = nil) async throws -> T {
        let httpBody = try MaayaJSON.encoder.encode(body)
        return try await request(port, path, method: method, httpBody: httpBody, timeout: timeout)
    }

    private func request<T: Decodable>(_ port: Int, _ path: String, method: String, httpBody: Data?,
                                       timeout: TimeInterval? = nil) async throws -> T {
        let data = try await perform(port, path, method: method, httpBody: httpBody,
                                     allowRefresh: true, timeout: timeout)
        return try MaayaJSON.decoder.decode(T.self, from: data)
    }

    // Generalized variant: arbitrary Content-Type (multipart, etc.) and returns
    // the raw bytes without JSON decoding — used by the voice endpoints, where
    // the request is multipart and the /speak response is audio. Shares the same
    // Bearer + single-silent-refresh contract as `perform`.
    private func performData(_ port: Int, _ path: String, method: String, body: Data?, contentType: String?,
                             allowRefresh: Bool = true, timeout: TimeInterval? = nil) async throws -> Data {
        let urlString = "\(AppConfig.moduleURL(port))\(path)"
        guard let url = URL(string: urlString) else { throw APIError.invalidURL(urlString) }

        var req = URLRequest(url: url)
        req.httpMethod = method
        // Gemma hears the audio natively and Kokoro renders the speech; both are slower
        // than a JSON read, and both run on the same box as the chat model.
        req.timeoutInterval = timeout ?? Self.voiceTimeout
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
                return try await performData(port, path, method: method, body: body, contentType: contentType, allowRefresh: false, timeout: timeout)
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

    private func perform(_ port: Int, _ path: String, method: String, httpBody: Data?, allowRefresh: Bool,
                         timeout: TimeInterval? = nil) async throws -> Data {
        let urlString = "\(AppConfig.moduleURL(port))\(path)"
        guard let url = URL(string: urlString) else { throw APIError.invalidURL(urlString) }

        var req = URLRequest(url: url)
        req.httpMethod = method
        req.timeoutInterval = timeout ?? Self.defaultTimeout
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
                return try await perform(port, path, method: method, httpBody: httpBody, allowRefresh: false, timeout: timeout)
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
