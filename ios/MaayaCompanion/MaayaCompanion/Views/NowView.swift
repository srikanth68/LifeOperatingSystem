import SwiftUI

// The home screen: what to do, not how things are.
//
// This replaces the Dashboard/Status/Health tabs, which reported net worth, module
// latency and sleep scores -- true things nobody opens a phone to read. Everything here
// is something the owner can finish with one tap, and finishing it is the point of the
// screen. Anything purely informational belongs on the website.
//
// Four sources, one list, ordered by when they need attention: San's reminders,
// today's unticked Karma habits, NorthStar's cross-module action queue, and active
// alerts. Everything except an alert can be finished from here.
struct NowView: View {
    let client: MaayaClient

    @State private var reminders: [ReminderItem] = []
    @State private var actions: [ActionItem] = []
    @State private var alerts: [AlertItem] = []
    @State private var habits: [KarmaHabit] = []
    @State private var loading = false
    @State private var errorText: String?
    @State private var busy: Set<String> = []       // ids mid-write, so taps can't double-fire
    @State private var showingNewReminder = false

    var body: some View {
        NavigationStack {
            List {
                if let errorText {
                    Section {
                        Label(errorText, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.orange)
                    }
                }

                if !dueReminders.isEmpty {
                    Section("Reminders") {
                        ForEach(dueReminders) { r in
                            row(id: r.id,
                                title: r.text,
                                subtitle: Self.due.string(from: r.dueAt),
                                overdue: r.dueAt < .now) {
                                try await client.setReminderDone(r.id, done: true)
                            }
                            // Snooze exists because "not now" is the honest answer more
                            // often than "done", and without it the only way to quiet an
                            // overdue reminder is to lie about having finished it.
                            .swipeActions(edge: .leading) {
                                Button("1 hour") {
                                    Task { await run(id: r.id) {
                                        try await client.snoozeReminder(r, to: .now.addingTimeInterval(3600))
                                    } }
                                }
                                .tint(.indigo)
                                Button("Tonight") {
                                    Task { await run(id: r.id) {
                                        try await client.snoozeReminder(r, to: Self.tonight())
                                    } }
                                }
                                .tint(.teal)
                            }
                        }
                    }
                }

                if !openHabits.isEmpty {
                    Section("Habits today") {
                        ForEach(openHabits) { h in
                            row(id: h.id,
                                title: "\(h.emoji) \(h.name)",
                                subtitle: h.currentStreak > 0 ? "\(h.currentStreak) day streak" : "",
                                overdue: false) {
                                try await client.logHabit(h.id, completed: true)
                            }
                        }
                    }
                }

                if !actions.isEmpty {
                    Section("On your list") {
                        ForEach(actions) { a in
                            row(id: a.id,
                                title: a.title,
                                subtitle: a.dueDate ?? a.category ?? a.source ?? "",
                                overdue: a.isUrgent) {
                                try await client.completeAction(a.id)
                            }
                        }
                    }
                }

                if !activeAlerts.isEmpty {
                    Section("Alerts") {
                        ForEach(activeAlerts) { al in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(al.title).font(.body)
                                if !al.description.isEmpty {
                                    Text(al.description)
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }

                if isEmpty && !loading {
                    // Deliberately not an empty grey box: "nothing to do" is a real and
                    // welcome answer, and it should read like one.
                    Section {
                        Label("Nothing needs you right now.", systemImage: "checkmark.circle")
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .navigationTitle("Now")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { showingNewReminder = true } label: {
                        Image(systemName: "plus")
                    }
                }
            }
            .refreshable { await load() }
            .task { await load() }
            .sheet(isPresented: $showingNewReminder) {
                NewReminderSheet(client: client) { await load() }
            }
        }
    }

    // MARK: - Pieces

    // One tappable line. The completion runs, then the item leaves the list — no
    // confirmation step, because every action here is reversible on the website and a
    // confirm tap on a to-do list is friction with no payoff.
    @ViewBuilder
    private func row(id: String,
                     title: String,
                     subtitle: String,
                     overdue: Bool,
                     complete: @escaping () async throws -> Void) -> some View {
        HStack(spacing: 12) {
            Button {
                Task { await run(id: id, complete) }
            } label: {
                Image(systemName: busy.contains(id) ? "circle.dotted" : "circle")
                    .font(.title3)
                    .foregroundStyle(overdue ? .orange : .secondary)
            }
            .buttonStyle(.plain)
            .disabled(busy.contains(id))

            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                if !subtitle.isEmpty {
                    Text(subtitle)
                        .font(.caption)
                        .foregroundStyle(overdue ? .orange : .secondary)
                }
            }
            Spacer(minLength: 0)
        }
        .opacity(busy.contains(id) ? 0.5 : 1)
    }

    // MARK: - Data

    private var dueReminders: [ReminderItem] {
        reminders.filter { !$0.done }.sorted { $0.dueAt < $1.dueAt }
    }
    // todayCompleted is optional in the API; treat unknown as not done, since the
    // cost of showing a habit you already ticked is one extra tap and the cost of
    // hiding one you have not is the whole point of the screen.
    private var openHabits: [KarmaHabit] {
        habits.filter { $0.todayCompleted != true }
    }
    private var activeAlerts: [AlertItem] {
        alerts.filter { $0.active }
    }
    private var isEmpty: Bool {
        dueReminders.isEmpty && actions.isEmpty && activeAlerts.isEmpty && openHabits.isEmpty
    }

    // 8pm today, or 8pm tomorrow if that has already passed — a "tonight" that has
    // gone by is a reminder that fires the instant you snooze it.
    private static func tonight() -> Date {
        let cal = Calendar.current
        let eight = cal.date(bySettingHour: 20, minute: 0, second: 0, of: .now) ?? .now
        return eight > .now ? eight : cal.date(byAdding: .day, value: 1, to: eight) ?? eight
    }

    private static let due: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "EEE d MMM, h:mm a"
        return f
    }()

    // Each source is fetched independently and failures are per-source: NorthStar being
    // unreachable should not blank out the reminders San answered for perfectly well.
    private func load() async {
        loading = true
        defer { loading = false }

        // Three round trips over the mesh, issued together rather than in sequence.
        async let r = client.reminders()
        async let a = client.pendingActions()
        async let al = client.alerts()
        async let hb = client.karmaToday()

        let rr = try? await r
        let aa = try? await a
        let alal = try? await al
        let hh = try? await hb

        reminders = rr ?? reminders
        actions   = aa ?? actions
        alerts    = alal ?? alerts
        habits    = hh ?? habits

        let down = [rr == nil ? "reminders" : nil,
                    aa == nil ? "actions" : nil,
                    alal == nil ? "alerts" : nil,
                    hh == nil ? "habits" : nil].compactMap { $0 }
        errorText = down.isEmpty ? nil : "Couldn't reach: \(down.joined(separator: ", "))"

        // The list the phone shows and the notifications it schedules come from the same
        // fetch, so opening this tab is also what keeps the local notifications current.
        await NotificationManager.shared.sync(using: client)
    }

    private func run(id: String, _ work: @escaping () async throws -> Void) async {
        busy.insert(id)
        defer { busy.remove(id) }
        do {
            try await work()
            await load()
        } catch {
            errorText = error.localizedDescription
        }
    }
}

// Creating a reminder from the phone, which is where "remind me to..." actually occurs
// to a person. Defaults to tomorrow morning rather than now, because a reminder for
// this instant is the one thing nobody wants.
private struct NewReminderSheet: View {
    let client: MaayaClient
    let onSaved: () async -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var text = ""
    @State private var dueAt = Calendar.current.date(
        bySettingHour: 9, minute: 0, second: 0,
        of: Calendar.current.date(byAdding: .day, value: 1, to: .now) ?? .now) ?? .now
    @State private var saving = false
    @State private var errorText: String?

    var body: some View {
        NavigationStack {
            Form {
                TextField("Remind me to…", text: $text, axis: .vertical)
                DatePicker("When", selection: $dueAt)
                if let errorText {
                    Text(errorText).foregroundStyle(.red).font(.caption)
                }
            }
            .navigationTitle("New reminder")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") { Task { await save() } }
                        .disabled(text.trimmingCharacters(in: .whitespaces).isEmpty || saving)
                }
            }
        }
    }

    private func save() async {
        saving = true
        defer { saving = false }
        do {
            try await client.createReminder(
                text: text.trimmingCharacters(in: .whitespacesAndNewlines), dueAt: dueAt)
            await onSaved()
            dismiss()
        } catch {
            errorText = error.localizedDescription
        }
    }
}
