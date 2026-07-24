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
            HKQuantityType(.bodyMass),
            HKQuantityType(.distanceWalkingRunning),
            HKCategoryType(.sleepAnalysis),
            HKObjectType.workoutType()
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
            return HealthPayload(steps: nil, heartRate: nil, activeCalories: nil, sleepHours: nil, sleepStart: nil, sleepEnd: nil, rawJson: nil)
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
            sleepHours: sleep.hours,
            sleepStart: sleep.start,
            sleepEnd: sleep.end,
            rawJson: nil
        )
    }

    // Everything needed for a full Vitara sync: today's snapshot plus latest
    // weight, the last week of workouts, and the last week of daily
    // step/calorie totals (so a day the phone missed still backfills).
    func fetchRichBundle(historyDays: Int = 7) async -> HealthKitBundle {
        let snapshot = await fetchTodayData()
        guard isAuthorized else {
            return HealthKitBundle(snapshot: snapshot, weightKg: nil, workouts: [], dailyActivity: [])
        }

        async let weight = fetchLatestWeightKg()
        async let workouts = fetchRecentWorkouts(days: historyDays)
        async let daily = fetchDailyActivity(days: historyDays)

        return HealthKitBundle(
            snapshot: snapshot,
            weightKg: await weight,
            workouts: await workouts,
            dailyActivity: await daily
        )
    }

    // MARK: - Private Fetch Methods

    private func fetchLatestWeightKg() async -> Double? {
        let type = HKQuantityType(.bodyMass)
        let sort = NSSortDescriptor(key: HKSampleSortIdentifierStartDate, ascending: false)
        return await withCheckedContinuation { continuation in
            let query = HKSampleQuery(sampleType: type, predicate: nil, limit: 1, sortDescriptors: [sort]) { _, samples, _ in
                guard let sample = samples?.first as? HKQuantitySample else {
                    continuation.resume(returning: nil)
                    return
                }
                let kg = sample.quantity.doubleValue(for: .gramUnit(with: .kilo))
                continuation.resume(returning: (kg * 10).rounded() / 10)
            }
            store.execute(query)
        }
    }

    private func fetchRecentWorkouts(days: Int) async -> [WorkoutPayload] {
        let start = Calendar.current.date(byAdding: .day, value: -days, to: Date())!
        let predicate = HKQuery.predicateForSamples(withStart: start, end: Date(), options: .strictStartDate)
        let sort = NSSortDescriptor(key: HKSampleSortIdentifierStartDate, ascending: false)
        return await withCheckedContinuation { continuation in
            let query = HKSampleQuery(sampleType: .workoutType(), predicate: predicate, limit: 50, sortDescriptors: [sort]) { _, samples, _ in
                guard let workouts = samples as? [HKWorkout] else {
                    continuation.resume(returning: [])
                    return
                }
                let mapped = workouts.map { w -> WorkoutPayload in
                    let cal = w.statistics(for: HKQuantityType(.activeEnergyBurned))?
                        .sumQuantity()?.doubleValue(for: .kilocalorie())
                    let dist = w.statistics(for: HKQuantityType(.distanceWalkingRunning))?
                        .sumQuantity()?.doubleValue(for: .meter())
                    return WorkoutPayload(
                        activity: Self.workoutName(w.workoutActivityType),
                        calories: cal.map { Int($0.rounded()) },
                        distanceMeters: dist.map { Int($0.rounded()) },
                        start: w.startDate,
                        end: w.endDate,
                        intensity: nil
                    )
                }
                continuation.resume(returning: mapped)
            }
            store.execute(query)
        }
    }

    // Daily step + active-calorie totals over the window, keyed by local day.
    private func fetchDailyActivity(days: Int) async -> [DailyActivityPayload] {
        async let stepsByDay = dailySums(HKQuantityType(.stepCount), unit: .count(), days: days)
        async let calsByDay = dailySums(HKQuantityType(.activeEnergyBurned), unit: .kilocalorie(), days: days)
        let (steps, cals) = await (stepsByDay, calsByDay)

        let fmt = DateFormatter()
        fmt.locale = Locale(identifier: "en_US_POSIX")
        fmt.dateFormat = "yyyy-MM-dd"

        let allDays = Set(steps.keys).union(cals.keys)
        return allDays.sorted().map { day in
            DailyActivityPayload(
                day: fmt.string(from: day),
                steps: steps[day].map { Int($0.rounded()) },
                activeCalories: cals[day].map { Int($0.rounded()) }
            )
        }
    }

    private func dailySums(_ type: HKQuantityType, unit: HKUnit, days: Int) async -> [Date: Double] {
        let calendar = Calendar.current
        let anchor = calendar.startOfDay(for: Date())
        let start = calendar.date(byAdding: .day, value: -(days - 1), to: anchor)!
        let predicate = HKQuery.predicateForSamples(withStart: start, end: Date(), options: .strictStartDate)

        return await withCheckedContinuation { continuation in
            let query = HKStatisticsCollectionQuery(
                quantityType: type,
                quantitySamplePredicate: predicate,
                options: .cumulativeSum,
                anchorDate: anchor,
                intervalComponents: DateComponents(day: 1)
            )
            query.initialResultsHandler = { _, results, _ in
                var out: [Date: Double] = [:]
                results?.enumerateStatistics(from: start, to: Date()) { stat, _ in
                    if let sum = stat.sumQuantity() {
                        out[calendar.startOfDay(for: stat.startDate)] = sum.doubleValue(for: unit)
                    }
                }
                continuation.resume(returning: out)
            }
            store.execute(query)
        }
    }

    private static func workoutName(_ type: HKWorkoutActivityType) -> String {
        switch type {
        case .running: return "Running"
        case .walking: return "Walking"
        case .cycling: return "Cycling"
        case .swimming: return "Swimming"
        case .traditionalStrengthTraining, .functionalStrengthTraining: return "Strength"
        case .highIntensityIntervalTraining: return "HIIT"
        case .yoga: return "Yoga"
        case .hiking: return "Hiking"
        case .elliptical: return "Elliptical"
        case .rowing: return "Rowing"
        case .coreTraining: return "Core"
        case .pilates: return "Pilates"
        case .cardioDance, .socialDance: return "Dance"
        case .tennis: return "Tennis"
        case .basketball: return "Basketball"
        case .soccer: return "Soccer"
        default: return "Workout"
        }
    }

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

    private func fetchLastNightSleep() async -> (hours: Double?, start: Date?, end: Date?) {
        let type = HKCategoryType(.sleepAnalysis)
        let calendar = Calendar.current
        let now = Date()
        let startOfToday = calendar.startOfDay(for: now)
        let startOfYesterday = calendar.date(byAdding: .day, value: -1, to: startOfToday)!

        // Look for sleep samples from 6 PM yesterday to now
        let windowStart = calendar.date(bySettingHour: 18, minute: 0, second: 0, of: startOfYesterday)!
        let predicate = HKQuery.predicateForSamples(withStart: windowStart, end: now, options: .strictStartDate)

        return await withCheckedContinuation { continuation in
            let query = HKSampleQuery(sampleType: type, predicate: predicate, limit: HKObjectQueryNoLimit, sortDescriptors: nil) { _, samples, error in
                guard let samples = samples as? [HKCategorySample] else {
                    continuation.resume(returning: (nil, nil, nil))
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
                let earliest = asleepSamples.map(\.startDate).min()
                let latest = asleepSamples.map(\.endDate).max()
                continuation.resume(returning: hours > 0 ? ((hours * 10).rounded() / 10, earliest, latest) : (nil, nil, nil))
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
