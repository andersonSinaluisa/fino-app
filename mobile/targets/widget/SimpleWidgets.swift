import SwiftUI
import WidgetKit

/// Shared timeline entry for every widget that has no per-instance
/// configuration (5 of the 7: Dinero disponible, Saldo total, Próximo pago,
/// Gastos del mes, Proyección). "Presupuesto" y "Cuenta" son configurables
/// (a category / an account to show) and live in ConfigurableWidgets.swift.
struct FinoEntry: TimelineEntry {
    let date: Date
    let snapshot: FinoWidgetSnapshot?
}

struct FinoProvider: TimelineProvider {
    func placeholder(in context: Context) -> FinoEntry {
        FinoEntry(date: Date(), snapshot: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (FinoEntry) -> Void) {
        completion(FinoEntry(date: Date(), snapshot: FinoWidgetStore.load()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<FinoEntry>) -> Void) {
        let entry = FinoEntry(date: Date(), snapshot: FinoWidgetStore.load())
        // The app pushes a fresh snapshot (and calls ExtensionStorage.reloadWidget())
        // every time the data it's built from changes, so this policy only
        // covers the case where the app hasn't been opened in a while --
        // refresh at most once an hour so a stale "Gastos del mes" doesn't
        // linger indefinitely if a push was missed.
        let nextRefresh = Calendar.current.date(byAdding: .hour, value: 1, to: Date()) ?? Date()
        completion(Timeline(entries: [entry], policy: .after(nextRefresh)))
    }
}

/// Small, centered label used by every "no data yet" / "logged out" state so
/// they read the same way across all 7 widgets -- styled with Fino's own
/// muted mark + textSecondary instead of the system's default secondary style.
struct FinoEmptyState: View {
    let title: String
    let systemImage: String

    var body: some View {
        VStack(spacing: 6) {
            WidgetMark(systemImage: systemImage, fallbackTint: Color("surfaceSecondary"))
            Text(title)
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Color("textSecondary"))
                .multilineTextAlignment(.center)
        }
        .padding()
    }
}

private struct WidgetChrome<Content: View>: View {
    let snapshot: FinoWidgetSnapshot?
    let link: FinoWidgetSnapshot.Link?
    @ViewBuilder let content: () -> Content

    var body: some View {
        Group {
            if snapshot == nil {
                FinoEmptyState(title: "Sin datos todavía", systemImage: "hourglass")
            } else if snapshot?.isAuthenticated == false {
                FinoEmptyState(title: "Inicia sesión en Fino", systemImage: "lock")
            } else {
                content()
            }
        }
        .widgetURL(link.flatMap { URL(string: $0.uri) })
        // Fino's actual card surface (theme/tokens.ts colors.surface), not the
        // system's default widget background -- so the card reads the same
        // white-on-paper look the rest of the app uses.
        .containerBackground(Color("surface"), for: .widget)
    }
}

/// Every fixed widget's header row: a colored mark (SF Symbol in a soft-tint
/// circle) + the eyebrow label, laid out identically across all 5 so the
/// family reads as one system.
private struct WidgetHeader: View {
    let systemImage: String
    let tint: Color
    let label: String

    var body: some View {
        HStack(spacing: 6) {
            WidgetMark(systemImage: systemImage, fallbackTint: tint)
            WidgetEyebrow(text: label)
        }
    }
}

// MARK: - 1. Dinero disponible

struct AvailableMoneyWidgetView: View {
    let snapshot: FinoWidgetSnapshot

    var body: some View {
        let data = snapshot.availableMoney
        VStack(alignment: .leading, spacing: 4) {
            WidgetHeader(systemImage: "wallet.pass.fill", tint: Color("accentLime"), label: "Dinero disponible")
            if data.hasData {
                WidgetValue(text: FinoFormat.money(data.amount, currency: data.currency, hidden: snapshot.amountsHidden))
                if data.isEstimated {
                    WidgetSubtitle(text: "Incluye saldos estimados")
                }
            } else {
                WidgetSubtitle(text: "Sin cuentas todavía")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
    }
}

struct AvailableMoneyWidget: Widget {
    let kind = "AvailableMoneyWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: FinoProvider()) { entry in
            WidgetChrome(snapshot: entry.snapshot, link: entry.snapshot?.availableMoney.link) {
                AvailableMoneyWidgetView(snapshot: entry.snapshot!)
            }
        }
        .configurationDisplayName("Dinero disponible")
        .description("Cuánto puedes gastar ahora mismo (sin contar tarjetas de crédito).")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - 2. Saldo total

struct TotalBalanceWidgetView: View {
    let snapshot: FinoWidgetSnapshot

    var body: some View {
        let data = snapshot.totalBalance
        VStack(alignment: .leading, spacing: 4) {
            WidgetHeader(systemImage: "banknote.fill", tint: Color("accentMint"), label: "Saldo total")
            if data.hasData {
                WidgetValue(text: FinoFormat.money(data.amount, currency: data.currency, hidden: snapshot.amountsHidden))
                WidgetSubtitle(text: data.accountCount == 1 ? "1 cuenta" : "\(data.accountCount) cuentas")
            } else {
                WidgetSubtitle(text: "Sin cuentas todavía")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
    }
}

struct TotalBalanceWidget: Widget {
    let kind = "TotalBalanceWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: FinoProvider()) { entry in
            WidgetChrome(snapshot: entry.snapshot, link: entry.snapshot?.totalBalance.link) {
                TotalBalanceWidgetView(snapshot: entry.snapshot!)
            }
        }
        .configurationDisplayName("Saldo total")
        .description("La suma de todas tus cuentas en Fino.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - 3. Próximo pago

struct NextPaymentWidgetView: View {
    let snapshot: FinoWidgetSnapshot

    var body: some View {
        let data = snapshot.nextPayment
        VStack(alignment: .leading, spacing: 4) {
            WidgetHeader(systemImage: "calendar", tint: Color("accentMint"), label: "Próximo pago")
            if data.hasData {
                Text(data.concept)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(Color("textPrimary"))
                    .lineLimit(1)
                WidgetValue(text: FinoFormat.money(data.amount, currency: data.currency, hidden: snapshot.amountsHidden))
                WidgetSubtitle(text: "Estimado: \(FinoFormat.shortDate(data.estimatedDate))")
            } else {
                WidgetSubtitle(text: "No detectamos pagos recurrentes todavía")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
    }
}

struct NextPaymentWidget: Widget {
    let kind = "NextPaymentWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: FinoProvider()) { entry in
            WidgetChrome(snapshot: entry.snapshot, link: entry.snapshot?.nextPayment.link) {
                NextPaymentWidgetView(snapshot: entry.snapshot!)
            }
        }
        .configurationDisplayName("Próximo pago")
        .description("El pago recurrente más reciente que Fino detectó, con fecha estimada.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - 4. Gastos del mes

struct MonthExpensesWidgetView: View {
    let snapshot: FinoWidgetSnapshot

    var body: some View {
        let data = snapshot.monthExpenses
        VStack(alignment: .leading, spacing: 4) {
            WidgetHeader(systemImage: "chart.line.downtrend.xyaxis", tint: Color("accentMint"), label: "Gastos del mes")
            if data.hasData {
                WidgetValue(text: FinoFormat.money(data.amount, currency: data.currency, hidden: snapshot.amountsHidden))
                if let percent = FinoFormat.percent(data.changePercent) {
                    WidgetSubtitle(text: "\(percent) vs. mes pasado")
                }
            } else {
                WidgetSubtitle(text: "Sin movimientos este mes")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
    }
}

struct MonthExpensesWidget: Widget {
    let kind = "MonthExpensesWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: FinoProvider()) { entry in
            WidgetChrome(snapshot: entry.snapshot, link: entry.snapshot?.monthExpenses.link) {
                MonthExpensesWidgetView(snapshot: entry.snapshot!)
            }
        }
        .configurationDisplayName("Gastos del mes")
        .description("Cuánto llevas gastado este mes, comparado con el anterior.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - 7. Proyección

struct ProjectionWidgetView: View {
    let snapshot: FinoWidgetSnapshot

    var body: some View {
        let data = snapshot.projection
        VStack(alignment: .leading, spacing: 4) {
            WidgetHeader(systemImage: "chart.line.uptrend.xyaxis", tint: Color("accentLime"), label: "Proyección")
            if data.hasData {
                WidgetValue(text: FinoFormat.money(data.projectedBalance, currency: data.currency, hidden: snapshot.amountsHidden))
                WidgetSubtitle(text: "Estimado a fin de mes")
            } else {
                WidgetSubtitle(text: "Sin datos suficientes todavía")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
    }
}

struct ProjectionWidget: Widget {
    let kind = "ProjectionWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: FinoProvider()) { entry in
            WidgetChrome(snapshot: entry.snapshot, link: entry.snapshot?.projection.link) {
                ProjectionWidgetView(snapshot: entry.snapshot!)
            }
        }
        .configurationDisplayName("Proyección")
        .description("Saldo total estimado para fin de mes, según tu ritmo de gasto actual.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}
