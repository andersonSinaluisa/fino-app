import React from 'react';
import { formatCurrency, formatShortDate } from '../../../utils/format';
import { ANDROID_WIDGET_NAMES, type AndroidWidgetName } from '../constants';
import type { WidgetSnapshot } from '../types';
import { WidgetCard, WidgetEmptyState } from './WidgetCard';

/**
 * One render function per widget kind -- the Android equivalent of the 7
 * SwiftUI views in targets/widget/*.swift. Both the live push
 * (sync.android.ts's requestWidgetUpdate) and the headless OS-triggered
 * task handler (taskHandler.ts) call these with the same snapshot shape, so
 * there is exactly one place that decides what each widget looks like.
 */

function loggedOutOrEmpty(snapshot: WidgetSnapshot, clickUri: string | null): React.JSX.Element | null {
  if (!snapshot.isAuthenticated) {
    return <WidgetEmptyState message="Inicia sesión en Fino" clickUri={clickUri} />;
  }
  return null;
}

export function renderAvailableMoney(snapshot: WidgetSnapshot): React.JSX.Element {
  const data = snapshot.availableMoney;
  const loggedOut = loggedOutOrEmpty(snapshot, data.link?.uri ?? null);
  if (loggedOut) return loggedOut;

  if (!data.hasData) {
    return <WidgetEmptyState message="Sin cuentas todavía" clickUri={data.link?.uri ?? null} />;
  }

  return (
    <WidgetCard
      eyebrow="Dinero disponible"
      value={formatCurrency(data.amount, { hidden: snapshot.amountsHidden })}
      subtitle={data.isEstimated ? 'Incluye saldos estimados' : undefined}
      clickUri={data.link?.uri ?? null}
    />
  );
}

export function renderTotalBalance(snapshot: WidgetSnapshot): React.JSX.Element {
  const data = snapshot.totalBalance;
  const loggedOut = loggedOutOrEmpty(snapshot, data.link?.uri ?? null);
  if (loggedOut) return loggedOut;

  if (!data.hasData) {
    return <WidgetEmptyState message="Sin cuentas todavía" clickUri={data.link?.uri ?? null} />;
  }

  return (
    <WidgetCard
      eyebrow="Saldo total"
      value={formatCurrency(data.amount, { hidden: snapshot.amountsHidden })}
      subtitle={data.accountCount === 1 ? '1 cuenta' : `${data.accountCount} cuentas`}
      clickUri={data.link?.uri ?? null}
    />
  );
}

export function renderNextPayment(snapshot: WidgetSnapshot): React.JSX.Element {
  const data = snapshot.nextPayment;
  const loggedOut = loggedOutOrEmpty(snapshot, data.link?.uri ?? null);
  if (loggedOut) return loggedOut;

  if (!data.hasData) {
    return <WidgetEmptyState message="No detectamos pagos recurrentes todavía" clickUri={data.link?.uri ?? null} />;
  }

  return (
    <WidgetCard
      eyebrow="Próximo pago"
      title={data.concept}
      value={formatCurrency(data.amount, { hidden: snapshot.amountsHidden })}
      subtitle={`Estimado: ${formatShortDate(data.estimatedDate)}`}
      clickUri={data.link?.uri ?? null}
    />
  );
}

export function renderMonthExpenses(snapshot: WidgetSnapshot): React.JSX.Element {
  const data = snapshot.monthExpenses;
  const loggedOut = loggedOutOrEmpty(snapshot, data.link?.uri ?? null);
  if (loggedOut) return loggedOut;

  if (!data.hasData) {
    return <WidgetEmptyState message="Sin movimientos este mes" clickUri={data.link?.uri ?? null} />;
  }

  const percent =
    data.changePercent === null
      ? undefined
      : `${data.changePercent >= 0 ? '+' : ''}${Math.round(data.changePercent)}% vs. mes pasado`;

  return (
    <WidgetCard
      eyebrow="Gastos del mes"
      value={formatCurrency(data.amount, { hidden: snapshot.amountsHidden })}
      subtitle={percent}
      clickUri={data.link?.uri ?? null}
    />
  );
}

export function renderProjection(snapshot: WidgetSnapshot): React.JSX.Element {
  const data = snapshot.projection;
  const loggedOut = loggedOutOrEmpty(snapshot, data.link?.uri ?? null);
  if (loggedOut) return loggedOut;

  if (!data.hasData) {
    return <WidgetEmptyState message="Sin datos suficientes todavía" clickUri={data.link?.uri ?? null} />;
  }

  return (
    <WidgetCard
      eyebrow="Proyección"
      value={formatCurrency(data.projectedBalance, { hidden: snapshot.amountsHidden })}
      subtitle="Estimado a fin de mes"
      clickUri={data.link?.uri ?? null}
    />
  );
}

/** "Presupuesto" -- `selectedCategoryId` comes from the widget's own configuration screen (configurationScreen.tsx). */
export function renderCategorySpend(snapshot: WidgetSnapshot, selectedCategoryId: string | null): React.JSX.Element {
  const loggedOut = loggedOutOrEmpty(snapshot, null);
  if (loggedOut) return loggedOut;

  const item =
    (selectedCategoryId && snapshot.categorySpend.find((c) => c.categoryId === selectedCategoryId)) ||
    snapshot.categorySpend[0];

  if (!item) {
    return <WidgetEmptyState message="Sin gastos en categorías este mes" clickUri={null} />;
  }

  return (
    <WidgetCard
      eyebrow="Presupuesto"
      title={item.categoryName}
      value={formatCurrency(item.amount, { hidden: snapshot.amountsHidden })}
      subtitle="Gastado este mes"
      clickUri={item.link?.uri ?? null}
    />
  );
}

/** "Cuenta" -- `selectedAccountId` comes from the widget's own configuration screen (configurationScreen.tsx). */
export function renderAccount(snapshot: WidgetSnapshot, selectedAccountId: string | null): React.JSX.Element {
  const loggedOut = loggedOutOrEmpty(snapshot, null);
  if (loggedOut) return loggedOut;

  const item =
    (selectedAccountId && snapshot.accounts.find((a) => a.accountId === selectedAccountId)) || snapshot.accounts[0];

  if (!item) {
    return <WidgetEmptyState message="Sin cuentas todavía" clickUri={null} />;
  }

  return (
    <WidgetCard
      eyebrow="Cuenta"
      title={item.alias}
      value={formatCurrency(item.amount, { hidden: snapshot.amountsHidden })}
      subtitle={item.isEstimated ? 'Saldo estimado' : 'Saldo verificado'}
      clickUri={item.link?.uri ?? null}
    />
  );
}

/**
 * Dispatches by `widgetName` (the AppWidgetProvider identity react-native-
 * android-widget's config plugin registers -- see ANDROID_WIDGET_NAMES).
 * `selectedId` only applies to the two configurable widgets; ignored by the others.
 */
export function renderAndroidWidget(
  widgetName: AndroidWidgetName,
  snapshot: WidgetSnapshot,
  selectedId: string | null = null,
): React.JSX.Element {
  switch (widgetName) {
    case ANDROID_WIDGET_NAMES.availableMoney:
      return renderAvailableMoney(snapshot);
    case ANDROID_WIDGET_NAMES.totalBalance:
      return renderTotalBalance(snapshot);
    case ANDROID_WIDGET_NAMES.nextPayment:
      return renderNextPayment(snapshot);
    case ANDROID_WIDGET_NAMES.monthExpenses:
      return renderMonthExpenses(snapshot);
    case ANDROID_WIDGET_NAMES.projection:
      return renderProjection(snapshot);
    case ANDROID_WIDGET_NAMES.categorySpend:
      return renderCategorySpend(snapshot, selectedId);
    case ANDROID_WIDGET_NAMES.account:
      return renderAccount(snapshot, selectedId);
    default:
      return <WidgetEmptyState message="Sin datos todavía" clickUri={null} />;
  }
}
