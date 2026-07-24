import SwiftUI

// Full-screen "voice call" with San. Hands-free: it listens, detects when you
// stop talking, replies out loud, and listens again — no buttons between turns.
// Tap the orb while San is speaking to cut in; Mute pauses the mic; End hangs up.
struct CallView: View {
    let client: MaayaClient
    var onEnd: () -> Void          // let the parent refresh chat history after a call

    @State private var manager: VoiceConversationManager
    @State private var pulse = false

    init(client: MaayaClient, onEnd: @escaping () -> Void) {
        self.client = client
        self.onEnd = onEnd
        _manager = State(initialValue: VoiceConversationManager(client: client))
    }

    var body: some View {
        ZStack {
            MaayaTheme.bg.ignoresSafeArea()

            VStack(spacing: 28) {
                Spacer()
                orb
                statusLabel
                transcript
                Spacer()
                controls
            }
            .padding(24)
        }
        .task { await manager.start() }
        .onDisappear { manager.stop() }
        .sensoryFeedback(trigger: manager.phase) { _, new in
            switch new {
            case .listening: return .impact(weight: .light)
            case .speaking:  return .impact(weight: .medium)
            default:         return nil
            }
        }
    }

    // MARK: - Orb

    private var orb: some View {
        ZStack {
            // Outer aura scales with live mic level while listening, gentle pulse otherwise.
            Circle()
                .fill(accent.opacity(0.18))
                .frame(width: 260, height: 260)
                .scaleEffect(auraScale)
                .animation(.easeOut(duration: 0.12), value: manager.level)
                .animation(.easeInOut(duration: 1.1).repeatForever(autoreverses: true), value: pulse)

            Circle()
                .fill(
                    RadialGradient(colors: [accent.opacity(0.9), accent.opacity(0.35)],
                                   center: .center, startRadius: 6, endRadius: 110)
                )
                .frame(width: 180, height: 180)
                .shadow(color: accent.opacity(0.6), radius: 30)

            Image(systemName: orbIcon)
                .font(.system(size: 52, weight: .semibold))
                .foregroundStyle(.black.opacity(0.8))
        }
        .contentShape(Circle())
        .onTapGesture { manager.interrupt() }   // barge-in while speaking
        .onAppear { pulse = true }
    }

    private var auraScale: CGFloat {
        switch manager.phase {
        case .listening: return 1.0 + CGFloat(manager.level) * 0.6
        case .speaking:  return pulse ? 1.15 : 0.95
        default:         return 1.0
        }
    }

    private var orbIcon: String {
        switch manager.phase {
        case .listening:            return "waveform"
        case .transcribing, .thinking: return "ellipsis"
        case .speaking:             return "speaker.wave.3.fill"
        case .muted:                return "mic.slash.fill"
        case .error:                return "exclamationmark.triangle.fill"
        case .idle:                 return "phone.fill"
        }
    }

    private var accent: Color {
        switch manager.phase {
        case .speaking:  return MaayaTheme.gold
        case .muted:     return .gray
        case .error:     return .orange
        default:         return MaayaTheme.vitara
        }
    }

    // MARK: - Labels & transcript

    private var statusLabel: some View {
        Text(statusText)
            .font(.headline)
            .foregroundStyle(.white.opacity(0.9))
            .animation(.default, value: manager.phase)
    }

    private var statusText: String {
        switch manager.phase {
        case .idle:          return "Connecting…"
        case .listening:     return "Listening…"
        case .transcribing:  return "Got it…"
        case .thinking:      return "San is thinking…"
        case .speaking:      return "San is speaking — tap to cut in"
        case .muted:         return "Muted"
        case .error(let m):  return m
        }
    }

    private var transcript: some View {
        VStack(spacing: 14) {
            if !manager.lastUserText.isEmpty {
                line(label: "You", text: manager.lastUserText, color: .white.opacity(0.75))
            }
            if !manager.lastSanText.isEmpty {
                line(label: "San", text: manager.lastSanText, color: MaayaTheme.gold)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 8)
        .frame(minHeight: 120, alignment: .top)
    }

    private func line(label: String, text: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label.uppercased())
                .font(.caption2).fontWeight(.bold)
                .foregroundStyle(color.opacity(0.7))
            Text(text)
                .font(.subheadline)
                .foregroundStyle(color)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    // MARK: - Controls

    private var controls: some View {
        HStack(spacing: 40) {
            circleButton(icon: manager.isMuted ? "mic.slash.fill" : "mic.fill",
                         tint: manager.isMuted ? .gray : .white,
                         bg: MaayaTheme.surface) {
                manager.toggleMute()
            }
            circleButton(icon: "phone.down.fill", tint: .white, bg: .red) {
                manager.stop()
                onEnd()
            }
        }
        .padding(.bottom, 12)
    }

    private func circleButton(icon: String, tint: Color, bg: Color, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 24, weight: .semibold))
                .foregroundStyle(tint)
                .frame(width: 64, height: 64)
                .background(bg, in: Circle())
        }
    }
}
