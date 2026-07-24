import Foundation

// Owns the auth session: probe → PIN/credentials login, token storage in the
// Keychain, and silent refresh. Mirrors the web frontend's services/auth.ts
// flow. Auth is served by Maaya.Auth, mounted on Vault (port 5000).
@Observable
final class AuthService {
    private static let accessKey = "maaya_access_token"
    private static let refreshKey = "maaya_refresh_token"
    private static let userKey = "maaya_username"

    private(set) var isAuthenticated: Bool
    private(set) var username: String?

    // A refresh already in flight — coalesces concurrent 401s onto one call.
    private var refreshTask: Task<Bool, Never>?

    init() {
        let hasToken = KeychainStore.get(Self.accessKey) != nil
        isAuthenticated = hasToken
        username = UserDefaults.standard.string(forKey: Self.userKey)
    }

    var accessToken: String? { KeychainStore.get(Self.accessKey) }

    private var authBase: String { "\(AppConfig.moduleURL(ModulePort.vault))/api/auth" }

    // MARK: - Login flow

    func probe() async -> ProbeResult {
        guard let url = URL(string: "\(authBase)/probe") else {
            return ProbeResult(trusted: false, method: "credentials", pinLength: 0)
        }
        do {
            let (data, response) = try await URLSession.shared.data(from: url)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else {
                return ProbeResult(trusted: false, method: "credentials", pinLength: 0)
            }
            return try MaayaJSON.decoder.decode(ProbeResult.self, from: data)
        } catch {
            return ProbeResult(trusted: false, method: "credentials", pinLength: 0)
        }
    }

    func login(username: String, password: String) async throws {
        let tokens = try await post("/login", body: LoginRequestBody(username: username, password: password))
        save(tokens)
    }

    func pinLogin(_ pin: String) async throws {
        let tokens = try await post("/pin", body: PinRequestBody(pin: pin))
        save(tokens)
    }

    // MARK: - Refresh

    // Coalesced silent refresh. Returns true if we now hold a fresh access token.
    func refresh() async -> Bool {
        if let task = refreshTask { return await task.value }
        let task = Task<Bool, Never> {
            guard let refreshToken = KeychainStore.get(Self.refreshKey) else { return false }
            do {
                let tokens: AuthTokens = try await self.post("/refresh", body: RefreshRequestBody(refreshToken: refreshToken))
                self.save(tokens)
                return true
            } catch {
                return false
            }
        }
        refreshTask = task
        let result = await task.value
        refreshTask = nil
        return result
    }

    func logout() async {
        if let token = accessToken, let refreshToken = KeychainStore.get(Self.refreshKey),
           let url = URL(string: "\(authBase)/logout") {
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            req.httpBody = try? MaayaJSON.encoder.encode(RefreshRequestBody(refreshToken: refreshToken))
            _ = try? await URLSession.shared.data(for: req)   // best-effort
        }
        clear()
    }

    // Called when a refresh fails on a 401 — drop the (now invalid) session.
    func forceLogout() { clear() }

    // MARK: - Storage

    private func save(_ tokens: AuthTokens) {
        KeychainStore.set(tokens.accessToken, for: Self.accessKey)
        KeychainStore.set(tokens.refreshToken, for: Self.refreshKey)
        UserDefaults.standard.set(tokens.username, forKey: Self.userKey)
        username = tokens.username
        isAuthenticated = true
    }

    private func clear() {
        KeychainStore.delete(Self.accessKey)
        KeychainStore.delete(Self.refreshKey)
        UserDefaults.standard.removeObject(forKey: Self.userKey)
        username = nil
        isAuthenticated = false
    }

    private func post<B: Encodable>(_ path: String, body: B) async throws -> AuthTokens {
        guard let url = URL(string: "\(authBase)\(path)") else { throw APIError.invalidURL(authBase + path) }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try MaayaJSON.encoder.encode(body)
        req.timeoutInterval = 30

        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw APIError.invalidResponse }
        guard (200...299).contains(http.statusCode) else {
            let message = (try? MaayaJSON.decoder.decode(AuthErrorBody.self, from: data))?.error
                ?? "Authentication failed (\(http.statusCode))"
            throw AuthError.rejected(message)
        }
        return try MaayaJSON.decoder.decode(AuthTokens.self, from: data)
    }
}

private struct AuthErrorBody: Codable { let error: String }

enum AuthError: LocalizedError {
    case rejected(String)
    var errorDescription: String? {
        switch self {
        case .rejected(let m): return m
        }
    }
}
