import Foundation
import Capacitor
import HealthKit

// Apple Health, read by Vitara's own app instead of the Maaya companion.
//
// The reading is the companion's (ios/MaayaCompanion/.../HealthManager.swift), moved here so
// one app on the phone does it. It returns the same bundle the companion posts to
// /api/healthkit/ingest; the web side posts it to /api/healthkit/sync, signed in like every
// other call, so no device key lives in the app.
//
// Read-only. HealthKit never says whether a read was refused (that would leak that the data
// exists), so "allowed" here means only that the question was asked; an empty bundle is the
// sign it was declined.
@objc(VitaraHealthPlugin)
public class VitaraHealthPlugin: CAPPlugin, CAPBridgedPlugin {
    public let identifier = "VitaraHealthPlugin"
    public let jsName = "VitaraHealth"
    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "isAvailable", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "requestAuthorization", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "readBundle", returnType: CAPPluginReturnPromise)
    ]

    private let store = HKHealthStore()

    private var readTypes: Set<HKObjectType> {
        Set([
            HKQuantityType(.stepCount),
            HKQuantityType(.heartRate),
            HKQuantityType(.activeEnergyBurned),
            HKQuantityType(.bodyMass),
            HKQuantityType(.bodyFatPercentage),
            HKQuantityType(.leanBodyMass),
            HKQuantityType(.distanceWalkingRunning),
            HKCategoryType(.sleepAnalysis),
            HKObjectType.workoutType()
        ])
    }

    @objc func isAvailable(_ call: CAPPluginCall) {
        call.resolve(["available": HKHealthStore.isHealthDataAvailable()])
    }

    @objc func requestAuthorization(_ call: CAPPluginCall) {
        guard HKHealthStore.isHealthDataAvailable() else {
            call.reject("Apple Health is not available on this device.")
            return
        }
        store.requestAuthorization(toShare: [], read: readTypes) { ok, error in
            if let error = error { call.reject(error.localizedDescription); return }
            call.resolve(["asked": ok])
        }
    }

    // Everything for one sync: today's steps, calories and latest heart rate, last night's
    // sleep, the latest weight and composition, and `days` of workouts and daily totals so
    // a day the phone was not opened still fills in.
    @objc func readBundle(_ call: CAPPluginCall) {
        let days = max(1, min(call.getInt("days") ?? 7, 90))
        Task {
            async let steps = self.sumToday(HKQuantityType(.stepCount), unit: .count())
            async let calories = self.sumToday(HKQuantityType(.activeEnergyBurned), unit: .kilocalorie())
            async let heart = self.latest(HKQuantityType(.heartRate)) { $0.quantity.doubleValue(for: HKUnit.count().unitDivided(by: .minute())) }
            async let sleep = self.lastNightSleep()
            async let weight = self.latest(HKQuantityType(.bodyMass)) { $0.quantity.doubleValue(for: .gramUnit(with: .kilo)) }
            async let fat = self.latest(HKQuantityType(.bodyFatPercentage)) { $0.quantity.doubleValue(for: .percent()) * 100 }
            async let lean = self.latest(HKQuantityType(.leanBodyMass)) { $0.quantity.doubleValue(for: .gramUnit(with: .kilo)) }
            async let workouts = self.workouts(days: days)
            async let daily = self.dailyActivity(days: days)

            var out: [String: Any] = ["timestamp": Self.iso(Date())]
            if let v = await steps { out["steps"] = Int(v.rounded()) }
            if let v = await calories { out["activeCalories"] = Int(v.rounded()) }
            if let v = await heart { out["heartRate"] = Int(v.rounded()) }
            let s = await sleep
            if let h = s.hours, let a = s.start, let b = s.end {
                out["sleepHours"] = h; out["sleepStart"] = Self.iso(a); out["sleepEnd"] = Self.iso(b)
            }
            if let v = await weight { out["weightKg"] = Self.tenth(v) }
            if let v = await fat { out["bodyFatPct"] = Self.tenth(v) }
            if let v = await lean { out["leanMassKg"] = Self.tenth(v) }
            out["workouts"] = await workouts
            out["dailyActivity"] = await daily
            call.resolve(out)
        }
    }

    // MARK: - Reads

    private func sumToday(_ type: HKQuantityType, unit: HKUnit) async -> Double? {
        let start = Calendar.current.startOfDay(for: Date())
        let predicate = HKQuery.predicateForSamples(withStart: start, end: Date(), options: .strictStartDate)
        return await withCheckedContinuation { c in
            let q = HKStatisticsQuery(quantityType: type, quantitySamplePredicate: predicate, options: .cumulativeSum) { _, result, _ in
                c.resume(returning: result?.sumQuantity()?.doubleValue(for: unit))
            }
            store.execute(q)
        }
    }

    private func latest(_ type: HKQuantityType, _ value: @escaping (HKQuantitySample) -> Double) async -> Double? {
        let sort = NSSortDescriptor(key: HKSampleSortIdentifierStartDate, ascending: false)
        return await withCheckedContinuation { c in
            let q = HKSampleQuery(sampleType: type, predicate: nil, limit: 1, sortDescriptors: [sort]) { _, samples, _ in
                c.resume(returning: (samples?.first as? HKQuantitySample).map(value))
            }
            store.execute(q)
        }
    }

    // Asleep time from 6 pm yesterday to now, any stage; "in bed" is not sleep.
    private func lastNightSleep() async -> (hours: Double?, start: Date?, end: Date?) {
        let cal = Calendar.current
        let yesterday = cal.date(byAdding: .day, value: -1, to: cal.startOfDay(for: Date()))!
        let from = cal.date(bySettingHour: 18, minute: 0, second: 0, of: yesterday)!
        let predicate = HKQuery.predicateForSamples(withStart: from, end: Date(), options: .strictStartDate)
        let asleep: Set<Int> = [
            HKCategoryValueSleepAnalysis.asleepCore.rawValue, HKCategoryValueSleepAnalysis.asleepDeep.rawValue,
            HKCategoryValueSleepAnalysis.asleepREM.rawValue, HKCategoryValueSleepAnalysis.asleepUnspecified.rawValue
        ]
        return await withCheckedContinuation { c in
            let q = HKSampleQuery(sampleType: HKCategoryType(.sleepAnalysis), predicate: predicate, limit: HKObjectQueryNoLimit, sortDescriptors: nil) { _, samples, _ in
                let s = (samples as? [HKCategorySample] ?? []).filter { asleep.contains($0.value) }
                let seconds = s.reduce(0.0) { $0 + $1.endDate.timeIntervalSince($1.startDate) }
                guard seconds > 0 else { c.resume(returning: (nil, nil, nil)); return }
                c.resume(returning: ((seconds / 360).rounded() / 10, s.map(\.startDate).min(), s.map(\.endDate).max()))
            }
            store.execute(q)
        }
    }

    private func workouts(days: Int) async -> [[String: Any]] {
        let start = Calendar.current.date(byAdding: .day, value: -days, to: Date())!
        let predicate = HKQuery.predicateForSamples(withStart: start, end: Date(), options: .strictStartDate)
        let sort = NSSortDescriptor(key: HKSampleSortIdentifierStartDate, ascending: false)
        return await withCheckedContinuation { c in
            let q = HKSampleQuery(sampleType: .workoutType(), predicate: predicate, limit: 100, sortDescriptors: [sort]) { _, samples, _ in
                let list = (samples as? [HKWorkout] ?? []).map { w -> [String: Any] in
                    var row: [String: Any] = [
                        "activity": Self.workoutName(w.workoutActivityType),
                        "start": Self.iso(w.startDate),
                        "end": Self.iso(w.endDate)
                    ]
                    if let kcal = w.statistics(for: HKQuantityType(.activeEnergyBurned))?.sumQuantity()?.doubleValue(for: .kilocalorie()) {
                        row["calories"] = Int(kcal.rounded())
                    }
                    if let m = w.statistics(for: HKQuantityType(.distanceWalkingRunning))?.sumQuantity()?.doubleValue(for: .meter()) {
                        row["distanceMeters"] = Int(m.rounded())
                    }
                    return row
                }
                c.resume(returning: list)
            }
            store.execute(q)
        }
    }

    private func dailyActivity(days: Int) async -> [[String: Any]] {
        async let steps = dailySums(HKQuantityType(.stepCount), unit: .count(), days: days)
        async let cals = dailySums(HKQuantityType(.activeEnergyBurned), unit: .kilocalorie(), days: days)
        let (s, k) = await (steps, cals)
        let fmt = DateFormatter()
        fmt.locale = Locale(identifier: "en_US_POSIX")
        fmt.dateFormat = "yyyy-MM-dd"
        return Set(s.keys).union(k.keys).sorted().map { day in
            var row: [String: Any] = ["day": fmt.string(from: day)]
            if let v = s[day] { row["steps"] = Int(v.rounded()) }
            if let v = k[day] { row["activeCalories"] = Int(v.rounded()) }
            return row
        }
    }

    private func dailySums(_ type: HKQuantityType, unit: HKUnit, days: Int) async -> [Date: Double] {
        let cal = Calendar.current
        let anchor = cal.startOfDay(for: Date())
        let start = cal.date(byAdding: .day, value: -(days - 1), to: anchor)!
        let predicate = HKQuery.predicateForSamples(withStart: start, end: Date(), options: .strictStartDate)
        return await withCheckedContinuation { c in
            let q = HKStatisticsCollectionQuery(quantityType: type, quantitySamplePredicate: predicate,
                                                options: .cumulativeSum, anchorDate: anchor, intervalComponents: DateComponents(day: 1))
            q.initialResultsHandler = { _, results, _ in
                var out: [Date: Double] = [:]
                results?.enumerateStatistics(from: start, to: Date()) { stat, _ in
                    if let sum = stat.sumQuantity() { out[cal.startOfDay(for: stat.startDate)] = sum.doubleValue(for: unit) }
                }
                c.resume(returning: out)
            }
            store.execute(q)
        }
    }

    // MARK: - Helpers

    private static let isoFormatter = ISO8601DateFormatter()
    private static func iso(_ d: Date) -> String { isoFormatter.string(from: d) }
    private static func tenth(_ v: Double) -> Double { (v * 10).rounded() / 10 }

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
}
