import Foundation

final class APIClient {
    private var baseURL: String {
        UserDefaults.standard.string(forKey: "serverURL") ?? "http://localhost:5300"
    }

    private var apiKey: String? {
        UserDefaults.standard.string(forKey: "apiKey")
    }

    private lazy var encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }()

    private lazy var decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

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
}

enum APIError: LocalizedError {
    case invalidURL(String)
    case invalidResponse
    case serverError(statusCode: Int, message: String)

    var errorDescription: String? {
        switch self {
        case .invalidURL(let url):
            return "Invalid server URL: \(url)"
        case .invalidResponse:
            return "Invalid response from server"
        case .serverError(let code, let message):
            return "Server error (\(code)): \(message)"
        }
    }
}
