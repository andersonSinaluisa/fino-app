import Foundation

enum FinoFormat {
    /// Mirrors the app's own money formatting (`mobile/utils/format.ts`
    /// `formatMoney`, `$1,234.56`-style) closely enough for a widget label --
    /// exact locale/currency-symbol edge cases are left to the full app UI.
    static func money(_ amount: Double, currency: String, hidden: Bool) -> String {
        if hidden {
            return "••••••"
        }

        let formatter = NumberFormatter()
        formatter.numberStyle = .currency
        formatter.currencyCode = currency
        formatter.maximumFractionDigits = 2
        formatter.minimumFractionDigits = 2

        return formatter.string(from: NSNumber(value: amount)) ?? "$\(amount)"
    }

    static func shortDate(_ isoString: String) -> String {
        let isoFormatter = ISO8601DateFormatter()
        isoFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        var date = isoFormatter.date(from: isoString)
        if date == nil {
            isoFormatter.formatOptions = [.withInternetDateTime]
            date = isoFormatter.date(from: isoString)
        }
        guard let resolved = date else { return "" }

        let display = DateFormatter()
        display.dateFormat = "d MMM"
        display.locale = Locale(identifier: "es_EC")
        return display.string(from: resolved)
    }

    static func percent(_ value: Double?) -> String? {
        guard let value else { return nil }
        let sign = value >= 0 ? "+" : ""
        return "\(sign)\(Int(value.rounded()))%"
    }
}
