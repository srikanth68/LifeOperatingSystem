import SwiftUI
import UIKit

// Holding the screen open while San is the thing being used.
//
// iOS sleeps the display after the system idle timer, and sleeping the display
// suspends this app -- there is no `audio` background mode here, deliberately, because
// a hands-free call that keeps running with the phone in a pocket is a different
// product from the one this is. The consequence is that a voice call simply ended
// mid-sentence after a few minutes of the user not touching the glass, which is
// precisely when a hands-free call is working as intended.
//
// Counted rather than a bare boolean. Two screens can want the display awake at once --
// a call opened from the chat tab is exactly that -- and with a plain flag whichever
// one disappeared first would switch it off under the other. Each holder names itself,
// the timer is disabled while any name is held, and it goes back the moment the last
// one lets go.
@MainActor
enum KeepAwake {
    private static var holders: Set<String> = []

    static func hold(_ reason: String) {
        holders.insert(reason)
        apply()
    }

    static func release(_ reason: String) {
        holders.remove(reason)
        apply()
    }

    // The safety net, called when the app stops being frontmost.
    //
    // iOS already ignores the idle timer for an app that is not in front, so this is
    // not what stops a backgrounded app burning the screen. It is what stops a stale
    // holder -- a view whose onDisappear never ran -- from silently keeping the display
    // awake the next time the app comes forward.
    static func releaseAll() {
        holders.removeAll()
        apply()
    }

    static var isHeld: Bool { !holders.isEmpty }

    private static func apply() {
        UIApplication.shared.isIdleTimerDisabled = !holders.isEmpty
    }
}

private struct KeepAwakeModifier: ViewModifier {
    @Environment(\.scenePhase) private var scenePhase

    let reason: String
    let active: Bool

    func body(content: Content) -> some View {
        content
            .onAppear { update() }
            .onChange(of: active) { _, _ in update() }
            // Self-sufficient rather than relying on the app-level net: a modifier that
            // only lets go on disappear would still be holding if a tab switch did not
            // produce one.
            .onChange(of: scenePhase) { _, _ in update() }
            .onDisappear { KeepAwake.release(reason) }
    }

    private func update() {
        if active && scenePhase == .active { KeepAwake.hold(reason) }
        else { KeepAwake.release(reason) }
    }
}

extension View {
    /// Keeps the display awake while this view is on screen and `active` is true.
    /// Releases on disappear, so a screen that is navigated away from cannot keep
    /// holding the phone open behind the user's back.
    func keepScreenAwake(_ reason: String, active: Bool = true) -> some View {
        modifier(KeepAwakeModifier(reason: reason, active: active))
    }
}
