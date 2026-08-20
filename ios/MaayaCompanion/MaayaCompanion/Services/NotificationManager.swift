import Foundation
import UserNotifications

// Mirrors San's reminders/alerts into on-device local notifications so the phone
// buzzes on time — natively, not just via Telegram. Local notifications need no
// Apple Developer account, no APNs, and no server: iOS delivers a scheduled
// UNCalendarNotificationTrigger even when the app is closed. Runs on every sync
// (foreground + ~15-min background refresh), scheduling future items and
// reconciling ones that changed. Nothing leaves the mesh beyond the normal
// authenticated reads to San.
@MainActor
@Observable
final class NotificationManager {
    static let shared = NotificationManager()
    private init() {}

    private(set) var authorized = false
    private let maxScheduled = 60          // iOS allows 64 pending; leave headroom

    func requestAuthorization() async {
        let granted = (try? await UNUserNotificationCenter.current()
            .requestAuthorization(options: [.alert, .sound, .badge])) ?? false
        authorized = granted
    }

    // Background entry point — builds its own client from the Keychain-stored session.
    func syncFromStoredAuth() async {
        await sync(using: MaayaClient(auth: AuthService()))
    }

    func sync(using client: MaayaClient) async {
        let center = UNUserNotificationCenter.current()
        let status = await center.notificationSettings().authorizationStatus
        guard status == .authorized || status == .provisional else { return }

        let reminders = (try? await client.reminders()) ?? []
        let alerts    = (try? await client.alerts()) ?? []

        await updateBadge(reminders: reminders)
        let now = Date()

        struct Pending { let id: String; let title: String; let body: String; let date: Date }
        var items: [Pending] = []

        for r in reminders where !r.done && r.dueAt > now {
            items.append(Pending(id: "reminder-\(r.id)", title: "Reminder", body: r.text, date: r.dueAt))
        }
        for a in alerts where a.active {
            if let t = a.triggerAt, t > now {
                items.append(Pending(id: "alert-\(a.id)", title: a.title,
                                     body: a.description.isEmpty ? "Alert" : a.description, date: t))
            }
        }
        items.sort { $0.date < $1.date }
        if items.count > maxScheduled { items = Array(items.prefix(maxScheduled)) }

        let desired = Set(items.map(\.id))
        for it in items { schedule(it.id, title: it.title, body: it.body, at: it.date, center: center) }

        // Drop ours that are no longer wanted (edited time, marked done, deleted).
        let pending = await center.pendingNotificationRequests()
        let stale = pending.map(\.identifier).filter {
            ($0.hasPrefix("reminder-") || $0.hasPrefix("alert-")) && !desired.contains($0)
        }
        if !stale.isEmpty { center.removePendingNotificationRequests(withIdentifiers: stale) }

        notifyNewlyTriggered(alerts, center: center)
    }

    private func schedule(_ id: String, title: String, body: String, at date: Date, center: UNUserNotificationCenter) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        let comps = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        let trigger = UNCalendarNotificationTrigger(dateMatching: comps, repeats: false)
        center.add(UNNotificationRequest(identifier: id, content: content, trigger: trigger), withCompletionHandler: nil)
    }

    // Threshold/goal-deadline alerts fire server-side (TriggeredAt gets set). Since
    // those aren't time-schedulable, surface each new trigger once, immediately, on
    // the sync that discovers it. Keyed by trigger timestamp so a re-arm re-fires.
    private func notifyNewlyTriggered(_ alerts: [AlertItem], center: UNUserNotificationCenter) {
        let key = "notifiedAlertTriggers"
        var seen = Set(UserDefaults.standard.stringArray(forKey: key) ?? [])
        var changed = false
        for a in alerts {
            guard let t = a.triggeredAt else { continue }
            let token = "\(a.id)@\(Int(t.timeIntervalSince1970))"
            if seen.contains(token) { continue }
            seen.insert(token); changed = true

            let content = UNMutableNotificationContent()
            content.title = a.title
            content.body = a.description.isEmpty ? "Alert triggered." : a.description
            content.sound = .default
            center.add(UNNotificationRequest(identifier: "fired-\(token)", content: content, trigger: nil),
                       withCompletionHandler: nil)   // nil trigger = deliver now
        }
        if changed { UserDefaults.standard.set(Array(seen), forKey: key) }
    }

    // The app icon carries the count of things already due.
    //
    // Without a paid developer account there is no push, so the phone can only ever
    // learn about work when the app runs. A badge is the one piece of that which
    // survives the app being closed -- it is visible on the home screen, costs no
    // notification permission beyond the one already granted, and answers "is there
    // anything?" without opening anything.
    //
    // Overdue only, deliberately. Badging everything outstanding produces a number
    // that never reaches zero, and a badge that is never zero is wallpaper.
    func updateBadge(reminders: [ReminderItem], actions: Int = 0) async {
        let overdue = reminders.filter { !$0.done && $0.dueAt <= .now }.count + actions
        try? await UNUserNotificationCenter.current().setBadgeCount(overdue)
    }
}

// Show notifications as banners even when the app is open in the foreground.
final class ForegroundNotificationDelegate: NSObject, UNUserNotificationCenterDelegate {
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .sound]
    }
}
