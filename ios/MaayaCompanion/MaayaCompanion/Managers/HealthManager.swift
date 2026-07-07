import Foundation
import HealthKit

@Observable
final class HealthManager {
    var isAuthorized = false
    var errorMessage: String?

    private let store = HKHealthStore()

    private var readTypes: Set<HKObjectType> {
        Set([
            HKQuantityType(.stepCount),
            HKQuantityType(.heartRate),
            HKQuantityType(.activeEnergyBurned),
            HKCategoryType(.sleepAnalysis)
        ])
    }

    var isAvailable: Bool {
        HKHealthStore.isHealthDataAvailable()
    }

    func requestAuthorization() async {
        guard isAvailable else {
            errorMessage = "HealthKit is not available on this device."
            return
        }

        do {
            try await store.requestAuthorization(toShare: [], read: readTypes)
            isAuthorized = true
        } catch {
            errorMessage = "Health access error: \(error.localizedDescription)"
            isAuthorized = false
        }
    }

    func fetchTodayData() async -> HealthPayload {
        guard isAuthorized else {
            return HealthPayload(steps: nil, heartRate: nil, activeCalories: nil, sleepHours: nil, rawJson: nil)
        }

        async let stepsResult = fetchTodaySteps()
        async let heartRateResult = fetchLatestHeartRate()
        async let caloriesResult = fetchTodayActiveCalories()
        async let sleepResult = fetchLastNightSleep()

        let (steps, heartRate, calories, sleep) = await (stepsResult, heartRateResult, caloriesResult, sleepResult)

        return HealthPayload(
            steps: steps,
            heartRate: heartRate,
            activeCalories: calories,
            sleepHours: sleep,
            rawJson: nil
        )
    }

    // MARK: - Private Fetch Methods

    private func fetchTodaySteps() async -> Int? {
        let type = HKQuantityType(.stepCount)
        let predicate = todayPredicate()

        return await withCheckedContinuation { continuation in
            let query = HKStatisticsQuery(quantityType: type, quantitySamplePredicate: predicate, options: .cumulativeSum) { _, result, error in
                guard let sum = result?.sumQuantity() else {
                    continuation.resume(returning: nil)
                    return
                }
                continuation.resume(returning: Int(sum.doubleValue(for: .count())))
            }
            store.execute(query)
        }
    }

    private func fetchLatestHeartRate() async -> Int? {
        let type = HKQuantityType(.heartRate)
        let sortDescriptor = NSSortDescriptor(key: HKSampleSortIdentifierStartDate, ascending: false)

        return await withCheckedContinuation { continuation in
            let query = HKSampleQuery(sampleType: type, predicate: nil, limit: 1, sortDescriptors: [sortDescriptor]) { _, samples, error in
                guard let sample = samples?.first as? HKQuantitySample else {
                    continuation.resume(returning: nil)
                    return
                }
                let bpm = sample.quantity.doubleValue(for: HKUnit.count().unitDivided(by: .minute()))
                continuation.resume(returning: Int(bpm))
            }
            store.execute(query)
        }
    }

    private func fetchTodayActiveCalories() async -> Int? {
        let type = HKQuantityType(.activeEnergyBurned)
        let predicate = todayPredicate()

        return await withCheckedContinuation { continuation in
            let query = HKStatisticsQuery(quantityType: type, quantitySamplePredicate: predicate, options: .cumulativeSum) { _, result, error in
                guard let sum = result?.sumQuantity() else {
                    continuation.resume(returning: nil)
                    return
                }
                continuation.resume(returning: Int(sum.doubleValue(for: .kilocalorie())))
            }
            store.execute(query)
        }
    }

    private func fetchLastNightSleep() async -> Double? {
        let type = HKCategoryType(.sleepAnalysis)
        let calendar = Calendar.current
        let now = Date()
        let startOfToday = calendar.startOfDay(for: now)
        let startOfYesterday = calendar.date(byAdding: .day, value: -1, to: startOfToday)!

        // Look for sleep samples from 6 PM yesterday to now
        let sleepStart = calendar.date(bySettingHour: 18, minute: 0, second: 0, of: startOfYesterday)!
        let predicate = HKQuery.predicateForSamples(withStart: sleepStart, end: now, options: .strictStartDate)

        return await withCheckedContinuation { continuation in
            let query = HKSampleQuery(sampleType: type, predicate: predicate, limit: HKObjectQueryNoLimit, sortDescriptors: nil) { _, samples, error in
                guard let samples = samples as? [HKCategorySample] else {
                    continuation.resume(returning: nil)
                    return
                }

                // Filter for asleep states (not inBed)
                let asleepSamples = samples.filter { sample in
                    sample.value == HKCategoryValueSleepAnalysis.asleepCore.rawValue ||
                    sample.value == HKCategoryValueSleepAnalysis.asleepDeep.rawValue ||
                    sample.value == HKCategoryValueSleepAnalysis.asleepREM.rawValue ||
                    sample.value == HKCategoryValueSleepAnalysis.asleepUnspecified.rawValue
                }

                let totalSeconds = asleepSamples.reduce(0.0) { total, sample in
                    total + sample.endDate.timeIntervalSince(sample.startDate)
                }

                let hours = totalSeconds / 3600.0
                continuation.resume(returning: hours > 0 ? (hours * 10).rounded() / 10 : nil)
            }
            store.execute(query)
        }
    }

    private func todayPredicate() -> NSPredicate {
        let calendar = Calendar.current
        let startOfDay = calendar.startOfDay(for: Date())
        return HKQuery.predicateForSamples(withStart: startOfDay, end: Date(), options: .strictStartDate)
    }
}
