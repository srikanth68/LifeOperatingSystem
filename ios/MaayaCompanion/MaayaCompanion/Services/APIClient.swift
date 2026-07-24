import Foundation

final class APIClient {
    // San (context/push) and Vitara (healthkit/ingest) are reached at the same
    // configured host on their own ports, and both authenticate with the device
    // key — NOT the JWT session. This keeps the existing background telemetry
    // path working exactly as before.
    private var baseURL: String { AppConfig.moduleURL(ModulePort.san) }
    private var vitaraURL: String { AppConfig.moduleURL(ModulePort.vitara) }
    private var apiKey: String? { AppConfig.deviceKey }

    private let encoder = MaayaJSON.encoder
    private let decoder = MaayaJSON.decoder

    func pushContext(_ request: ContextPushRequest) async throws -> ContextPushResult {
        let urlString = "\(baseURL)/api/context/push"
        guard let url = URL(string: urlString) else {
            throw APIError.invalidURL(urlString)
        }

        var urlRequest = URLRequest(url: url)
        urlRequest.httpMethod = "POST"
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")

        if let key = apiKey, !key.isEmpty {
            urlRequest.setValue(key, forHTTPHeaderField: "X-Device-Key")
        }

        urlRequest.httpBody = try encoder.encode(request)
        urlRequest.timeoutInterval = 30

        let (data, response) = try await URLSession.shared.data(for: urlRequest)

        guard let httpResponse = response as? HTTPURLResponse else {
            throw APIError.invalidResponse
        }

        guard (200...299).contains(httpResponse.statusCode) else {
            let body = String(data: data, encoding: .utf8) ?? "No response body"
            throw APIError.serverError(statusCode: httpResponse.statusCode, message: body)
        }

        return try decoder.decode(ContextPushResult.self, from: data)
    }

    // Push HealthKit metrics directly into Vitara's own tables. Sends the base
    // snapshot plus the richer bundle (weight, recent workouts, multi-day history).
    func pushHealthKit(_ bundle: HealthKitBundle) async throws -> HealthKitIngestResult {
        let urlString = "\(vitaraURL)/api/healthkit/ingest"
        guard let url = URL(string: urlString) else {
            throw APIError.invalidURL(urlString)
        }

        var urlRequest = URLRequest(url: url)
        urlRequest.httpMethod = "POST"
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let key = apiKey, !key.isEmpty {
            urlRequest.setValue(key, forHTTPHeaderField: "X-Device-Key")
        }

        let health = bundle.snapshot
        let body = HealthKitIngestRequest(
            steps: health.steps,
            heartRate: health.heartRate,
            activeCalories: health.activeCalories,
            sleepHours: health.sleepHours,
            sleepStart: health.sleepStart,
            sleepEnd: health.sleepEnd,
            timestamp: .now,
            weightKg: bundle.weightKg,
            workouts: bundle.workouts.isEmpty ? nil : bundle.workouts,
            dailyActivity: bundle.dailyActivity.isEmpty ? nil : bundle.dailyActivity
        )
        urlRequest.httpBody = try encoder.encode(body)
        urlRequest.timeoutInterval = 30

        let (data, response) = try await URLSession.shared.data(for: urlRequest)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw APIError.invalidResponse
        }
        guard (200...299).contains(httpResponse.statusCode) else {
            let msg = String(data: data, encoding: .utf8) ?? "No response body"
            throw APIError.serverError(statusCode: httpResponse.statusCode, message: msg)
        }
        return try decoder.decode(HealthKitIngestResult.self, from: data)
    }
}

enum APIError: LocalizedError {
    case invalidURL(String)
    case invalidResponse
    case serverError(statusCode: Int, message: String)
    case sessionExpired

    var errorDescription: String? {
        switch self {
        case .invalidURL(let url):
            return "Invalid server URL: \(url)"
        case .invalidResponse:
            return "Invalid response from server"
        case .serverError(let code, let message):
            return "Server error (\(code)): \(message)"
        case .sessionExpired:
            return "Session expired. Please sign in again."
        }
    }
}
