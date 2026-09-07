import WidgetKit
import SwiftUI

/// Fino's home-screen widgets. Every widget reads the same shared snapshot
/// (Snapshot.swift) the app writes via ExtensionStorage -- no widget talks
/// to the network or computes its own financial numbers.
@main
struct FinoWidgetsBundle: WidgetBundle {
    var body: some Widget {
        AvailableMoneyWidget()
        TotalBalanceWidget()
        NextPaymentWidget()
        MonthExpensesWidget()
        CategorySpendWidget()
        ProjectionWidget()
        AccountWidget()
    }
}
