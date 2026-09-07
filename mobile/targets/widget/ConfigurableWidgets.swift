import AppIntents
import SwiftUI
import WidgetKit

// MARK: - 5. Presupuesto (redefined: "gastado en la categoría este mes", sin límite/progreso -- Fino no tiene presupuestos)

struct CategoryEntity: AppEntity, Identifiable {
    let id: String
    let name: String

    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Categoría"
    static var defaultQuery = CategoryEntityQuery()

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: "\(name)")
    }
}

struct CategoryEntityQuery: EntityQuery {
    func entities(for identifiers: [CategoryEntity.ID]) async -> [CategoryEntity] {
        allCategories().filter { identifiers.contains($0.id) }
    }

    func suggestedEntities() async -> [CategoryEntity] {
        allCategories()
    }

    private func allCategories() -> [CategoryEntity] {
        (FinoWidgetStore.load()?.selectableCategories ?? []).map { CategoryEntity(id: $0.id, name: $0.name) }
    }
}

struct CategorySpendConfigurationIntent: WidgetConfigurationIntent {
    static var title: LocalizedStringResource { "Elegir categoría" }
    static var description: IntentDescription { "Qué categoría mostrar en el widget de Presupuesto." }

    @Parameter(title: "Categoría")
    var category: CategoryEntity?
}

struct CategorySpendEntry: TimelineEntry {
    let date: Date
    let snapshot: FinoWidgetSnapshot?
    let categoryId: String?
}

struct CategorySpendProvider: AppIntentTimelineProvider {
    func placeholder(in context: Context) -> CategorySpendEntry {
        CategorySpendEntry(date: Date(), snapshot: nil, categoryId: nil)
    }

    func snapshot(for configuration: CategorySpendConfigurationIntent, in context: Context) async -> CategorySpendEntry {
        CategorySpendEntry(date: Date(), snapshot: FinoWidgetStore.load(), categoryId: configuration.category?.id)
    }

    func timeline(for configuration: CategorySpendConfigurationIntent, in context: Context) async -> Timeline<CategorySpendEntry> {
        let entry = CategorySpendEntry(date: Date(), snapshot: FinoWidgetStore.load(), categoryId: configuration.category?.id)
        let nextRefresh = Calendar.current.date(byAdding: .hour, value: 1, to: Date()) ?? Date()
        return Timeline(entries: [entry], policy: .after(nextRefresh))
    }
}

struct CategorySpendWidgetView: View {
    let entry: CategorySpendEntry

    var body: some View {
        Group {
            if let snapshot = entry.snapshot, snapshot.isAuthenticated {
                if let item = selected(in: snapshot) {
                    VStack(alignment: .leading, spacing: 4) {
                        Label(item.categoryName, systemImage: "tag")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                        Text(FinoFormat.money(item.amount, currency: item.currency, hidden: snapshot.amountsHidden))
                            .font(.title2.bold())
                        Text("Gastado este mes")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding()
                    .widgetURL(item.link.flatMap { URL(string: $0.uri) })
                } else {
                    FinoEmptyState(title: "Sin gastos en categorías este mes", systemImage: "tag")
                }
            } else if entry.snapshot == nil {
                FinoEmptyState(title: "Sin datos todavía", systemImage: "hourglass")
            } else {
                FinoEmptyState(title: "Inicia sesión en Fino", systemImage: "lock")
            }
        }
        .containerBackground(.background, for: .widget)
    }

    /// The person's chosen category if it had spend this month, otherwise the
    /// top category by spend (also the default before they ever configure
    /// the widget) -- never a hidden/empty widget just because their pick
    /// didn't spend anything this particular month... falls back gracefully.
    private func selected(in snapshot: FinoWidgetSnapshot) -> FinoWidgetSnapshot.CategorySpend? {
        if let categoryId = entry.categoryId, let match = snapshot.categorySpend.first(where: { $0.categoryId == categoryId }) {
            return match
        }
        return snapshot.categorySpend.first
    }
}

struct CategorySpendWidget: Widget {
    let kind = "CategorySpendWidget"

    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: kind, intent: CategorySpendConfigurationIntent.self, provider: CategorySpendProvider()) { entry in
            CategorySpendWidgetView(entry: entry)
        }
        .configurationDisplayName("Presupuesto")
        .description("Lo gastado este mes en la categoría que elijas (Fino todavía no tiene límites de presupuesto).")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - 8. Cuenta

struct AccountEntity: AppEntity, Identifiable {
    let id: String
    let alias: String

    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Cuenta"
    static var defaultQuery = AccountEntityQuery()

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: "\(alias)")
    }
}

struct AccountEntityQuery: EntityQuery {
    func entities(for identifiers: [AccountEntity.ID]) async -> [AccountEntity] {
        allAccounts().filter { identifiers.contains($0.id) }
    }

    func suggestedEntities() async -> [AccountEntity] {
        allAccounts()
    }

    private func allAccounts() -> [AccountEntity] {
        (FinoWidgetStore.load()?.selectableAccounts ?? []).map { AccountEntity(id: $0.id, alias: $0.alias) }
    }
}

struct AccountConfigurationIntent: WidgetConfigurationIntent {
    static var title: LocalizedStringResource { "Elegir cuenta" }
    static var description: IntentDescription { "Qué cuenta mostrar en el widget." }

    @Parameter(title: "Cuenta")
    var account: AccountEntity?
}

struct AccountEntry: TimelineEntry {
    let date: Date
    let snapshot: FinoWidgetSnapshot?
    let accountId: String?
}

struct AccountProvider: AppIntentTimelineProvider {
    func placeholder(in context: Context) -> AccountEntry {
        AccountEntry(date: Date(), snapshot: nil, accountId: nil)
    }

    func snapshot(for configuration: AccountConfigurationIntent, in context: Context) async -> AccountEntry {
        AccountEntry(date: Date(), snapshot: FinoWidgetStore.load(), accountId: configuration.account?.id)
    }

    func timeline(for configuration: AccountConfigurationIntent, in context: Context) async -> Timeline<AccountEntry> {
        let entry = AccountEntry(date: Date(), snapshot: FinoWidgetStore.load(), accountId: configuration.account?.id)
        let nextRefresh = Calendar.current.date(byAdding: .hour, value: 1, to: Date()) ?? Date()
        return Timeline(entries: [entry], policy: .after(nextRefresh))
    }
}

struct AccountWidgetView: View {
    let entry: AccountEntry

    var body: some View {
        Group {
            if let snapshot = entry.snapshot, snapshot.isAuthenticated {
                if let item = selected(in: snapshot) {
                    VStack(alignment: .leading, spacing: 4) {
                        Label(item.alias, systemImage: "creditcard")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                        Text(FinoFormat.money(item.amount, currency: item.currency, hidden: snapshot.amountsHidden))
                            .font(.title2.bold())
                        Text(item.isEstimated ? "Saldo estimado" : "Saldo verificado")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding()
                    .widgetURL(item.link.flatMap { URL(string: $0.uri) })
                } else {
                    FinoEmptyState(title: "Sin cuentas todavía", systemImage: "creditcard")
                }
            } else if entry.snapshot == nil {
                FinoEmptyState(title: "Sin datos todavía", systemImage: "hourglass")
            } else {
                FinoEmptyState(title: "Inicia sesión en Fino", systemImage: "lock")
            }
        }
        .containerBackground(.background, for: .widget)
    }

    private func selected(in snapshot: FinoWidgetSnapshot) -> FinoWidgetSnapshot.AccountEntry? {
        if let accountId = entry.accountId, let match = snapshot.accounts.first(where: { $0.accountId == accountId }) {
            return match
        }
        return snapshot.accounts.first
    }
}

struct AccountWidget: Widget {
    let kind = "AccountWidget"

    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: kind, intent: AccountConfigurationIntent.self, provider: AccountProvider()) { entry in
            AccountWidgetView(entry: entry)
        }
        .configurationDisplayName("Cuenta")
        .description("El saldo de la cuenta que elijas, con su estado verificado o estimado.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}
