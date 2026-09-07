import SwiftUI

/// Fino's widget visual language, mirroring theme/tokens.ts and
/// components/ui/ProviderAvatar.tsx so a home-screen widget reads as
/// unmistakably Fino rather than a stock system tile.
///
/// The fixed palette (surface/textPrimary/textSecondary/accentLime/
/// accentMint/onAccent, plus $widgetBackground) lives in Assets.xcassets as
/// real color assets -- see expo-target.config.js's `colors` map, which is
/// this file's single source of truth and regenerates those assets on
/// prebuild. Only a *dynamic* per-instance color (a category or account's
/// real brand hex, which can't live in the static asset catalog) is parsed
/// at runtime here.
private struct RGB {
    let r: Double
    let g: Double
    let b: Double

    // Defining `init?(hex:)` below suppresses Swift's synthesized memberwise
    // initializer, so this one is written out explicitly -- `softTint`/
    // `legible` both need it to build a blended/darkened RGB from an already-
    // parsed one.
    init(r: Double, g: Double, b: Double) {
        self.r = r
        self.g = g
        self.b = b
    }

    init?(hex: String) {
        var value = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        value = value.hasPrefix("#") ? String(value.dropFirst()) : value
        guard value.count == 6, let intValue = UInt32(value, radix: 16) else { return nil }
        r = Double((intValue >> 16) & 0xFF)
        g = Double((intValue >> 8) & 0xFF)
        b = Double(intValue & 0xFF)
    }

    var color: Color {
        Color(red: r / 255, green: g / 255, blue: b / 255)
    }
}

enum FinoWidgetColor {
    /// Same soft-tint idea as ProviderAvatar's `withAlpha`: blends `hex`
    /// toward white at 16% opacity, expressed as a solid color since the
    /// widget card background is always white/`surface`.
    static func softTint(of hex: String?) -> Color {
        guard let hex, let rgb = RGB(hex: hex) else { return Color("surfaceSecondary") }
        let mix = { (channel: Double) in channel * 0.16 + 255 * 0.84 }
        return RGB(r: mix(rgb.r), g: mix(rgb.g), b: mix(rgb.b)).color
    }

    /// Mirrors ProviderAvatar's `darken`: keeps initials legible on pale
    /// brand colors (e.g. a bank's pale yellow) instead of washing out.
    static func legible(_ hex: String?) -> Color {
        guard let hex, let rgb = RGB(hex: hex) else { return Color("textPrimary") }
        let luminance = (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b) / 255
        guard luminance >= 0.55 else { return rgb.color }
        let factor = 0.45
        return RGB(r: rgb.r * factor, g: rgb.g * factor, b: rgb.b * factor).color
    }
}

/// First 1-2 initials of a name, same rule as utils/format.ts `initialsOf`.
func widgetInitials(of value: String) -> String {
    let words = value.trimmingCharacters(in: .whitespacesAndNewlines)
        .split(separator: " ")
        .prefix(2)
    let initials = words.compactMap { $0.first }.map { String($0).uppercased() }.joined()
    return initials.isEmpty ? "?" : initials
}

/// The small colored mark in a widget's header row: an SF Symbol in a soft-
/// tint circle for the 5 fixed widgets (a hand-picked icon + a fixed accent,
/// since they have no natural per-instance color), or a ProviderAvatar-style
/// initials mark for Presupuesto/Cuenta, which do have a real category/
/// account brand color.
struct WidgetMark: View {
    /// SF Symbol for the 5 fixed widgets (mutually exclusive with `initials`).
    var systemImage: String? = nil
    /// Category/account initials for Presupuesto/Cuenta (mutually exclusive with `systemImage`).
    var initials: String? = nil
    /// The real per-instance brand hex (category/account color), when there is one.
    var brandHex: String? = nil
    /// Fixed accent for the widgets with no per-instance color -- ignored once `brandHex` is set.
    var fallbackTint: Color = Color("accentMint")

    private var tint: Color {
        brandHex != nil ? FinoWidgetColor.softTint(of: brandHex) : fallbackTint
    }

    var body: some View {
        ZStack {
            Circle().fill(tint)
            if let initials {
                Text(initials)
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(brandHex != nil ? FinoWidgetColor.legible(brandHex) : Color("textSecondary"))
            } else if let systemImage {
                Image(systemName: systemImage)
                    .font(.system(size: 11, weight: .bold))
                    .foregroundStyle(Color("onAccent"))
            }
        }
        .frame(width: 22, height: 22)
    }
}

/// A widget's eyebrow label -- Fino's "overline" type style (theme/tokens.ts
/// typography.overline: 11pt, bold, wide tracking), always textSecondary.
struct WidgetEyebrow: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 11, weight: .bold))
            .tracking(0.5)
            .foregroundStyle(Color("textSecondary"))
            .lineLimit(1)
    }
}

/// A widget's headline number -- Fino's "heading" type style, always
/// textPrimary (never the system label color, which shifts in dark mode).
struct WidgetValue: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 20, weight: .bold))
            .tracking(-0.3)
            .foregroundStyle(Color("textPrimary"))
    }
}

/// A widget's supporting line -- Fino's "caption" type style.
struct WidgetSubtitle: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 12, weight: .medium))
            .foregroundStyle(Color("textSecondary"))
    }
}
