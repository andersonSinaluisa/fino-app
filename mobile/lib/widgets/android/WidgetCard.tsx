import React from 'react';
import { FlexWidget, TextWidget } from 'react-native-android-widget';
import { initialsOf } from '../../../utils/format';

/**
 * Shared building block for every Android widget's JSX (built with
 * react-native-android-widget's own primitives -- FlexWidget/TextWidget map
 * to native RemoteViews, so this can't use regular RN <View>/<Text>).
 * Mirrors the layout of the iOS widgets (targets/widget/SimpleWidgets.swift)
 * closely enough that the same widget looks like the same feature on both
 * platforms, without either platform importing the other's code.
 *
 * Visual language matches theme/tokens.ts -- Fino's actual card surface,
 * radius and type scale -- rather than a generic list-tile look, and the
 * small colored mark in the header reuses the exact soft-tint formula
 * components/ui/ProviderAvatar.tsx uses for a category/account's brand
 * color, so a home-screen widget reads as unmistakably Fino.
 */

// Literal copies of theme/tokens.ts -- react-native-android-widget's
// ColorProp type wants a `#${string}` literal, so these are copied rather
// than imported to keep that typing simple.
const CARD_BACKGROUND = '#FFFFFF'; // colors.surface
const TEXT_PRIMARY = '#191A18'; // colors.text
const TEXT_SECONDARY = '#74766F'; // colors.textSecondary
export const ACCENT_LIME = '#C7F36B'; // colors.accent -- reserved for "your money, right now"
export const ACCENT_MINT = '#8DD9B6'; // colors.accentSecondary -- everything else
const CARD_RADIUS = 22; // radius.lg, the same radius Card.tsx uses in-app
const MARK_FALLBACK_BG = '#ECE9E1'; // colors.surfaceSecondary

/**
 * Blends `hex` toward the (white) card background at `alpha` opacity and
 * returns a solid hex -- visually identical to a translucent overlay on a
 * white card, without depending on RemoteViews alpha-compositing support.
 * Same soft-tint idea as ProviderAvatar's `withAlpha`, expressed as a solid
 * color since the widget card background is always white.
 */
function tint(hex: string, alpha: number): `#${string}` {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  const mix = (channel: number) => Math.round(channel * alpha + 255 * (1 - alpha));
  const toHex = (channel: number) => mix(channel).toString(16).padStart(2, '0');
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
}

/** Mirrors ProviderAvatar's `darken`: keeps initials legible on pale brand colors. */
function legibleOnLight(hex: string): `#${string}` {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  if (luminance < 0.55) return hex as `#${string}`;
  const factor = 0.45;
  const scale = (value: number) => Math.round(value * factor).toString(16).padStart(2, '0');
  return `#${scale(r)}${scale(g)}${scale(b)}`;
}

function isHex(value: string | null | undefined): value is `#${string}` {
  return !!value && /^#[0-9a-fA-F]{6}$/.test(value);
}

/**
 * The small colored mark in the card's header row: a plain accent dot for
 * the 5 fixed widgets (no natural per-instance color), or a ProviderAvatar-
 * style initials mark for Presupuesto/Cuenta, which do have a real category/
 * account color to show.
 */
function Mark(props: { accent?: string | null; markLabel?: string | null }): React.JSX.Element {
  const { accent, markLabel } = props;
  const brand = isHex(accent) ? accent : null;

  if (markLabel) {
    return (
      <FlexWidget
        style={{
          width: 24,
          height: 24,
          borderRadius: 8,
          backgroundColor: brand ? tint(brand, 0.16) : MARK_FALLBACK_BG,
          justifyContent: 'center',
          alignItems: 'center',
        }}
      >
        <TextWidget
          text={initialsOf(markLabel)}
          style={{ fontSize: 10, fontWeight: '700', color: brand ? legibleOnLight(brand) : TEXT_SECONDARY }}
        />
      </FlexWidget>
    );
  }

  return (
    <FlexWidget
      style={{
        width: 8,
        height: 8,
        borderRadius: 4,
        backgroundColor: brand ?? ACCENT_MINT,
      }}
    />
  );
}

export function WidgetCard(props: {
  eyebrow: string;
  title?: string;
  value?: string;
  subtitle?: string;
  clickUri: string | null;
  /** ACCENT_LIME/ACCENT_MINT for the fixed widgets, or a real category/account
   * brand hex -- which also switches the mark to an initials avatar, see `markLabel`. */
  accent?: string | null;
  /** Category/account name: when set, the header mark becomes its initials
   * on a soft tint of `accent`, matching ProviderAvatar. */
  markLabel?: string | null;
}): React.JSX.Element {
  const { eyebrow, title, value, subtitle, clickUri, accent, markLabel } = props;

  return (
    <FlexWidget
      clickAction={clickUri ? 'OPEN_URI' : 'OPEN_APP'}
      clickActionData={clickUri ? { uri: clickUri } : undefined}
      style={{
        height: 'match_parent',
        width: 'match_parent',
        backgroundColor: CARD_BACKGROUND,
        borderRadius: CARD_RADIUS,
        padding: 16,
        flexDirection: 'column',
        justifyContent: 'center',
        alignItems: 'flex-start',
      }}
    >
      <FlexWidget style={{ flexDirection: 'row', alignItems: 'center', flexGap: 6, marginBottom: 6 }}>
        <Mark accent={accent} markLabel={markLabel} />
        <TextWidget
          text={eyebrow}
          truncate="END"
          maxLines={1}
          style={{ fontSize: 11, color: TEXT_SECONDARY, fontWeight: '700', letterSpacing: 0.5 }}
        />
      </FlexWidget>
      {title ? (
        <TextWidget
          text={title}
          truncate="END"
          maxLines={1}
          style={{ fontSize: 14, color: TEXT_PRIMARY, fontWeight: '600', marginBottom: 2 }}
        />
      ) : null}
      {value ? (
        <TextWidget text={value} style={{ fontSize: 22, color: TEXT_PRIMARY, fontWeight: '700', letterSpacing: -0.3 }} />
      ) : null}
      {subtitle ? (
        <TextWidget text={subtitle} style={{ fontSize: 12, color: TEXT_SECONDARY, fontWeight: '500', marginTop: 4 }} />
      ) : null}
    </FlexWidget>
  );
}

export function WidgetEmptyState(props: { message: string; clickUri: string | null }): React.JSX.Element {
  return (
    <FlexWidget
      clickAction={props.clickUri ? 'OPEN_URI' : 'OPEN_APP'}
      clickActionData={props.clickUri ? { uri: props.clickUri } : undefined}
      style={{
        height: 'match_parent',
        width: 'match_parent',
        backgroundColor: CARD_BACKGROUND,
        borderRadius: CARD_RADIUS,
        padding: 16,
        flexDirection: 'column',
        justifyContent: 'center',
        alignItems: 'center',
      }}
    >
      <FlexWidget style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: ACCENT_MINT, marginBottom: 8 }} />
      <TextWidget
        text={props.message}
        maxLines={3}
        style={{ fontSize: 12, color: TEXT_SECONDARY, fontWeight: '500', textAlign: 'center' }}
      />
    </FlexWidget>
  );
}
