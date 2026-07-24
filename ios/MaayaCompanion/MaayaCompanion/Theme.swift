import SwiftUI

// Maaya OS "command center" palette, matched to the web dashboard's dark/gold aesthetic.
enum MaayaTheme {
    static let gold    = Color(red: 0.831, green: 0.659, blue: 0.263)  // #d4a843
    static let goldLight = Color(red: 0.922, green: 0.792, blue: 0.447) // #ebca72
    static let vitara  = Color(red: 0.024, green: 0.784, blue: 0.627)  // #06c8a0 (health)
    static let bg      = Color(red: 0.024, green: 0.055, blue: 0.110)  // #060e1c
    static let surface = Color(red: 0.047, green: 0.094, blue: 0.188)  // #0c1830
    static let cash    = Color(red: 0.122, green: 0.784, blue: 0.478)  // #1fc87a
    static let border  = Color.white.opacity(0.08)
}

// Translucent glass card matching the web app's .glass-card look.
struct GlassCard: ViewModifier {
    var accent: Color = MaayaTheme.border
    func body(content: Content) -> some View {
        content
            .padding()
            .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 16))
            .overlay(
                RoundedRectangle(cornerRadius: 16)
                    .strokeBorder(accent.opacity(0.35), lineWidth: 1)
            )
            .shadow(color: .black.opacity(0.25), radius: 8, y: 4)
    }
}

extension View {
    func glassCard(accent: Color = MaayaTheme.border) -> some View {
        modifier(GlassCard(accent: accent))
    }
}
