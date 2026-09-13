import React from 'react';
import { formatCurrency, formatShortDate } from '../../../utils/format';
import { ANDROID_WIDGET_NAMES, type AndroidWidgetName } from '../constants';
import type { PulseWidgetData, WidgetSnapshot } from '../types';
import { FlexWidget, TextWidget } from 'react-native-android-widget';
import {
  ACCENT_LIME,
  ACCENT_MINT,
  CARD_BACKGROUND,
  CARD_RADIUS,
  SURFACE_SECONDARY,
  TEXT_PRIMARY,
  TEXT_SECONDARY,
  WidgetCard,
  WidgetEmptyState,
} from './WidgetCard';
import { widgetDeepLinks } from '../deepLinks';

// Literal copies of theme/tokens.ts colors.warning/colors.danger -- same
// reasoning as ACCENT_LIME/ACCENT_MINT above (ColorProp wants a
// `#${string}` literal, not an imported constant).
const ACCENT_WARNING = '#E4A853';
const ACCENT_DANGER = '#D8665B';

function accentForSeverity(severity: PulseWidgetData['severity']): `#${string}` {
  switch (severity) {
    case 'Positive':
      return ACCENT_MINT;
    case 'Attention':
      return ACCENT_WARNING;
    case 'Risk':
      return ACCENT_DANGER;
    default:
      return ACCENT_LIME;
  }
}

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
      accent={ACCENT_LIME}
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
      accent={ACCENT_MINT}
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
      accent={ACCENT_MINT}
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
      accent={ACCENT_MINT}
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
      accent={ACCENT_LIME}
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
      accent={item.categoryColor}
      markLabel={item.categoryName}
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
      accent={item.brandColor}
      markLabel={item.providerName ?? item.alias}
    />
  );
}

/**
 * "Pulso" -- the most relevant `FinancialPulse` right now (see
 * buildSnapshot.ts's `mapTopPulse`). No per-instance configuration, like
 * the other 5 fixed widgets -- there's exactly one "most relevant" pulse.
 */
export function renderPulse(snapshot: WidgetSnapshot): React.JSX.Element {
  const data = snapshot.pulse;
  const loggedOut = loggedOutOrEmpty(snapshot, data.link?.uri ?? null);
  if (loggedOut) return loggedOut;

  if (!data.hasData) {
    return <WidgetEmptyState message="Nada nuevo que contarte todavía" clickUri={data.link?.uri ?? null} />;
  }

  return (
    <WidgetCard
      eyebrow="Fino"
      title={data.title}
      subtitle={data.body}
      subtitleMaxLines={2}
      clickUri={data.link?.uri ?? null}
      accent={accentForSeverity(data.severity)}
    />
  );
}

/**
 * Dispatches by `widgetName` (the AppWidgetProvider identity react-native-
 * android-widget's config plugin registers -- see ANDROID_WIDGET_NAMES).
 * `selectedId` only applies to the two configurable widgets; ignored by the others.
 */
/**
 * §28: el equivalente Android del QuickEntryWidget de iOS. No lee el snapshot: no
 * muestra ningún dato, así que ni el saldo ni la sesión cambian lo que dibuja. Abre
 * el mismo `fino:///registrar` que el widget de iOS y que los atajos del icono.
 *
 * Un widget de Android solo admite una acción de clic por elemento, así que aquí el
 * botón grande escribe y la fila de abajo dicta -- dos FlexWidget con su propio
 * clickAction, no uno solo.
 */
export function renderQuickEntry(): React.JSX.Element {
  return (
    <FlexWidget
      style={{
        height: 'match_parent',
        width: 'match_parent',
        backgroundColor: CARD_BACKGROUND,
        borderRadius: CARD_RADIUS,
        padding: 12,
        flexDirection: 'column',
        justifyContent: 'center',
        alignItems: 'center',
        flexGap: 8,
      }}
    >
      <FlexWidget
        clickAction="OPEN_URI"
        clickActionData={{ uri: widgetDeepLinks.quickEntry() }}
        style={{
          width: 'match_parent',
          backgroundColor: ACCENT_LIME,
          borderRadius: 999,
          paddingVertical: 12,
          flexDirection: 'row',
          justifyContent: 'center',
          alignItems: 'center',
        }}
      >
        <TextWidget
          text="+ Registrar"
          style={{ fontSize: 15, color: TEXT_PRIMARY, fontWeight: '700' }}
        />
      </FlexWidget>

      <FlexWidget
        clickAction="OPEN_URI"
        clickActionData={{ uri: widgetDeepLinks.quickEntryByVoice() }}
        style={{
          width: 'match_parent',
          backgroundColor: SURFACE_SECONDARY,
          borderRadius: 999,
          paddingVertical: 8,
          flexDirection: 'row',
          justifyContent: 'center',
          alignItems: 'center',
        }}
      >
        <TextWidget text="Dictar" style={{ fontSize: 13, color: TEXT_SECONDARY, fontWeight: '600' }} />
      </FlexWidget>
    </FlexWidget>
  );
}

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
    case ANDROID_WIDGET_NAMES.pulse:
      return renderPulse(snapshot);
    case ANDROID_WIDGET_NAMES.quickEntry:
      return renderQuickEntry();
    default:
      return <WidgetEmptyState message="Sin datos todavía" clickUri={null} />;
  }
}
