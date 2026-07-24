import Foundation

// Mirrors Maaya.Auth's /api/auth contract (see shared/Maaya.Auth/AuthController.cs
// and the web frontend's services/auth.ts).

struct ProbeResult: Codable {
    let trusted: Bool
    let method: String        // "pin" | "credentials"
    let pinLength: Int
}

struct AuthTokens: Codable {
    let accessToken: String
    let refreshToken: String
    let expiresIn: Int
    let username: String
}

struct LoginRequestBody: Codable {
    let username: String
    let password: String
}

struct PinRequestBody: Codable {
    let pin: String
}

struct RefreshRequestBody: Codable {
    let refreshToken: String
}

enum AuthMethod {
    case pin(length: Int)
    case credentials
}
