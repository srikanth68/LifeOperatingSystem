import Foundation

// Single source of truth for how the app reaches Everest.
//
// The owner runs NordVPN Meshnet (or Tailscale) on the phone; that exposes the
// Mac mini at a private mesh IP (e.g. 100.126.41.41). We just talk plain HTTP
// to <scheme>://<host>:<port> per module, mirroring the web frontend's old
// per-port apiHost model. Host + scheme are configurable in Settings so the
// same build works whether the owner points at the mesh IP, a .nord hostname,
// or localhost during testing.
enum ModulePort {
    static let vault     = 5000
    static let vitara    = 5100
    static let aasthi    = 5200
    static let san       = 5300
    static let sutra     = 5400
    static let northstar = 5500
    static let karma     = 5600
    static let nexus     = 5700
}

enum AppConfig {
    // Keys are shared with SettingsView's @AppStorage bindings.
    static var host: String {
        let h = UserDefaults.standard.string(forKey: "serverHost") ?? ""
        return h.isEmpty ? "100.126.41.41" : h
    }

    static var scheme: String {
        let s = UserDefaults.standard.string(forKey: "serverScheme") ?? ""
        return s.isEmpty ? "http" : s
    }

    // Device key for the unauthenticated telemetry endpoints (San /context/push,
    // Vitara /healthkit/ingest). Matched server-side against DEVICE_API_KEY.
    // Legacy builds stored this under "apiKey"; keep reading that.
    static var deviceKey: String? {
        UserDefaults.standard.string(forKey: "apiKey")
    }

    static func moduleURL(_ port: Int) -> String {
        "\(scheme)://\(host):\(port)"
    }
}

// Shared JSON coders. The date decoding is deliberately lenient: the .NET
// backends serialize DateTime in several shapes depending on the source
// (UTC "…Z", with 7-digit fractional seconds, or Kind=Unspecified with no
// zone at all from SQLite). Foundation's built-in .iso8601 strategy rejects
// fractional seconds, so we parse flexibly and assume UTC when no zone is given.
enum MaayaJSON {
    static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.dateEncodingStrategy = .iso8601
        return e
    }()

    static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .custom { decoder in
            let raw = try decoder.singleValueContainer().decode(String.self)
            if let date = MaayaDate.parse(raw) { return date }
            throw DecodingError.dataCorrupted(
                .init(codingPath: decoder.codingPath, debugDescription: "Unparseable date: \(raw)"))
        }
        return d
    }()
}

enum MaayaDate {
    private static let isoFractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let iso: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    // Handles "yyyy-MM-dd'T'HH:mm:ss" and fractional variants with NO timezone
    // (SQLite DateTime Kind=Unspecified). Treated as UTC.
    private static let naive: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSSSSSS"
        return f
    }()

    private static let naivePlain: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        return f
    }()

    static func parse(_ s: String) -> Date? {
        isoFractional.date(from: s)
            ?? iso.date(from: s)
            ?? naive.date(from: s)
            ?? naivePlain.date(from: s)
    }
}
