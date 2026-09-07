import Foundation
import WidgetKit

/// Mirrors `mobile/lib/widgets/types.ts` `WidgetSnapshot` field-for-field
/// (only what the widgets actually render). Built once on the JS side from
/// real Fino data (`GET /summary`, `GET /analytics/dashboard`) and written
/// as a single JSON string via `ExtensionStorage` -- this file never talks
/// to the network or invents a value; it only decodes what the app already
/// computed and never contains a token, password, or account number.
struct FinoWidgetSnapshot: Decodable {
    struct Link: Decodable {
        let uri: String
    }

    struct AvailableMoney: Decodable {
        let hasData: Bool
        let link: Link?
        let amount: Double
        let currency: String
        let isEstimated: Bool
    }

    struct TotalBalance: Decodable {
        let hasData: Bool
        let link: Link?
        let amount: Double
        let currency: String
        let accountCount: Int
        let isEstimated: Bool
    }

    struct NextPayment: Decodable {
        let hasData: Bool
        let link: Link?
        let concept: String
        let categoryName: String?
        let amount: Double
        let currency: String
        let estimatedDate: String
        let occurrences: Int
    }

    struct MonthExpenses: Decodable {
        let hasData: Bool
        let link: Link?
        let amount: Double
        let currency: String
        let previousAmount: Double
        let changePercent: Double?
    }

    struct CategorySpend: Decodable {
        let hasData: Bool
        let link: Link?
        let categoryId: String?
        let categoryName: String
        let categoryIcon: String?
        let categoryColor: String?
        let amount: Double
        let currency: String
        let percentageOfMonth: Double
    }

    struct Projection: Decodable {
        let hasData: Bool
        let link: Link?
        let projectedBalance: Double
        let currentBalance: Double
        let currency: String
        let daysRemaining: Int
    }

    struct AccountEntry: Decodable {
        let hasData: Bool
        let link: Link?
        let accountId: String?
        let alias: String
        let providerName: String?
        let brandColor: String?
        let amount: Double
        let currency: String
        let isEstimated: Bool
    }

    struct SelectableCategory: Decodable {
        let id: String
        let name: String
        let icon: String?
        let color: String?
    }

    struct SelectableAccount: Decodable {
        let id: String
        let alias: String
        let providerName: String?
        let brandColor: String?
    }

    let version: Int
    let generatedAt: String
    let isAuthenticated: Bool
    let amountsHidden: Bool
    let currency: String
    let availableMoney: AvailableMoney
    let totalBalance: TotalBalance
    let nextPayment: NextPayment
    let monthExpenses: MonthExpenses
    let projection: Projection
    let categorySpend: [CategorySpend]
    let accounts: [AccountEntry]
    let selectableCategories: [SelectableCategory]
    let selectableAccounts: [SelectableAccount]
}

/// Reads the snapshot the main app wrote via `new ExtensionStorage(WIDGET_APP_GROUP).set("snapshot", json)`.
/// `ExtensionStorage.set` stores a plain JSON *string* value (not JSON-encoded
/// `Data`) when given a string, via `UserDefaults.set(_:forKey:)` -- so this
/// reads it back with `string(forKey:)`, matching that exact write path
/// (see node_modules/@bacons/apple-targets/ios/ExtensionStorageModule.swift).
enum FinoWidgetStore {
    /// Keep in sync with `mobile/lib/widgets/constants.ts` (`WIDGET_APP_GROUP`, `WIDGET_SNAPSHOT_KEY`).
    static let appGroup = "group.app.fino.mobile"
    static let snapshotKey = "snapshot"

    static func load() -> FinoWidgetSnapshot? {
        guard
            let raw = UserDefaults(suiteName: appGroup)?.string(forKey: snapshotKey),
            let data = raw.data(using: .utf8)
        else {
            return nil
        }

        return try? JSONDecoder().decode(FinoWidgetSnapshot.self, from: data)
    }
}
