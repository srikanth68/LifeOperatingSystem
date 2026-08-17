import Foundation

// Read-only decode models for each module's summary endpoint. Field names and
// shapes are taken from the real .NET DTOs/controllers (serialized camelCase by
// ASP.NET Core's default System.Text.Json). Numeric fields are Double? where the
// server may emit a rounded/fractional value, so decoding never fails on a
// number that isn't a whole integer.

// ── Vault — GET 5000/api/summary (DashboardSummaryDto) ──
struct VaultSummary: Codable {
    let netWorth: Double
    let totalCash: Double
    let totalDebt: Double
    let cashByInstitution: [InstitutionBalance]
    let debtByInstitution: [InstitutionBalance]
}

struct InstitutionBalance: Codable, Identifiable {
    let institutionName: String
    let totalBalance: Double
    let accounts: [AccountBalance]
    var id: String { institutionName }
}

struct AccountBalance: Codable, Identifiable {
    let name: String
    let subType: String
    let balance: Double
    var id: String { name + subType }
}

// ── Vitara — GET 5100/api/dashboard (anonymous object) ──
struct VitaraDashboard: Codable {
    let date: String?
    let sleep: Sleep?
    let readiness: Readiness?
    let activity: Activity?
    let vo2Max: Double?
    let cardiovascularAge: Double?

    struct Sleep: Codable {
        let score: Double?
        let totalMinutes: Double?
        let efficiency: Double?
        let hrv: Double?
        let lowestHr: Double?
    }
    struct Readiness: Codable {
        let score: Double?
        let level: String?
        let restingHr: Double?
    }
    struct Activity: Codable {
        let score: Double?
        let steps: Double?
        let activeCalories: Double?
        let totalCalories: Double?
    }
}

// ── Aasthi — GET 5200/api/properties/summary (PortfolioSummary) ──
struct AasthiSummary: Codable {
    let propertyCount: Int
    let totalPurchasePrice: Double
    let totalCurrentValue: Double
    let totalProfit: Double
    let totalProfitPct: Double?
}

// ── Sutra — GET 5400/api/documents/stats (StatsResult) ──
struct SutraStats: Codable {
    let totalCount: Int
    let totalSize: String
    let byCategory: [String: Int]
    let expiringSoon: Int
}

// ── Karma — GET 5600/api/habits/today (HabitResult[]) ──
struct KarmaHabit: Codable, Identifiable {
    let id: String
    let name: String
    let emoji: String
    let category: String
    let currentStreak: Int
    let bestStreak: Int
    let todayCompleted: Bool?
}

// ── NorthStar — GET 5500/api/dashboard (DashboardResult) ──
struct NorthStarDashboard: Codable {
    let totalEntries: Int
    let entriesBySource: [String: Int]
    let recentInsights: [Insight]
    let recentEntries: [KnowledgeEntry]

    struct Insight: Codable, Identifiable {
        let id: String
        let title: String
        let body: String
        let createdAt: Date
    }
    struct KnowledgeEntry: Codable, Identifiable {
        let id: String
        let source: String
        let topic: String
        let summary: String
        let createdAt: Date
    }
}

// ── Nexus — GET 5700/api/nexus/sentinel/status (StatusDto) + /board (BoardRow[]) ──
struct NexusStatus: Codable {
    let schemaVersion: Int
    let lastRunAt: Date?
    let marketOpen: Bool
    let trackedCount: Int
    let openAlerts24h: Int
}

struct NexusBoardRow: Codable, Identifiable {
    let symbol: String
    let price: Double
    let changePct: Double?
    let action: String
    let conviction: Int
    let freshness: String
    var id: String { symbol }
}

// ── San chat — GET/POST 5300/api/chat/messages ──
struct ChatMessage: Codable, Identifiable, Equatable {
    let id: String
    let role: String
    let content: String
    let createdAt: Date
}

struct ChatSendResult: Codable {
    let userMessage: ChatMessage
    let assistantMessage: ChatMessage
    let provider: String
    let model: String
}

// mode is what switches the server onto its spoken path: a "voice" turn is offered a
// curated 8 tools instead of the full catalogue, gets the speak-aloud output rules, a
// smaller history budget, and its own llama.cpp cache slot. Sending nothing meant every
// call from the phone was handled as a typed turn -- so San read markdown out loud, and
// took the slow route doing it.
//
// imageDataUrl is a "data:image/jpeg;base64,..." string. The server has accepted these
// all along; the app simply never offered one.
struct ChatSendBody: Codable {
    let content: String
    var imageDataUrl: String?
    var mode: String?
}
