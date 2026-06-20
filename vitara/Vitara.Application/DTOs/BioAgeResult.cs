namespace Vitara.Application.DTOs;

public record BioAgeResult(
    double? BioAge,
    int ChronologicalAge,
    double? Delta,              // BioAge - ChronologicalAge (negative = younger)
    BioAgeFactors Factors,
    string DataQuality          // "good" | "limited" | "insufficient"
);

public record BioAgeFactors(
    double? HrvScore,           // 0-100, higher HRV = younger
    double? RestingHrScore,     // 0-100, lower RHR = younger
    double? SleepScore,         // avg Oura sleep score
    double? ReadinessScore,     // avg Oura readiness score
    double? RecoveryTrend       // 30-day trend slope
);
