import React from 'react';
import { FlexWidget, TextWidget } from 'react-native-android-widget';

/**
 * Shared building block for every Android widget's JSX (built with
 * react-native-android-widget's own primitives -- FlexWidget/TextWidget map
 * to native RemoteViews, so this can't use regular RN <View>/<Text>).
 * Mirrors the layout of the iOS widgets (targets/widget/SimpleWidgets.swift)
 * closely enough that the same widget looks like the same feature on both
 * platforms, without either platform importing the other's code.
 */

// Literal copies of theme/tokens.ts colors.surface/text/textSecondary --
// react-native-android-widget's ColorProp type wants a `#${string}` literal,
// so these are copied rather than imported to keep that typing simple.
const CARD_BACKGROUND = '#FFFFFF';
const TEXT_PRIMARY = '#191A18';
const TEXT_SECONDARY = '#74766F';

export function WidgetCard(props: {
  eyebrow: string;
  title?: string;
  value?: string;
  subtitle?: string;
  clickUri: string | null;
}): React.JSX.Element {
  const { eyebrow, title, value, subtitle, clickUri } = props;

  return (
    <FlexWidget
      clickAction={clickUri ? 'OPEN_URI' : 'OPEN_APP'}
      clickActionData={clickUri ? { uri: clickUri } : undefined}
      style={{
        height: 'match_parent',
        width: 'match_parent',
        backgroundColor: CARD_BACKGROUND,
        borderRadius: 16,
        padding: 16,
        flexDirection: 'column',
        justifyContent: 'center',
        alignItems: 'flex-start',
      }}
    >
      <TextWidget
        text={eyebrow}
        style={{ fontSize: 12, color: TEXT_SECONDARY, fontWeight: '600', marginBottom: 4 }}
      />
      {title ? (
        <TextWidget
          text={title}
          truncate="END"
          maxLines={1}
          style={{ fontSize: 14, color: TEXT_PRIMARY, fontWeight: '600', marginBottom: 2 }}
        />
      ) : null}
      {value ? (
        <TextWidget text={value} style={{ fontSize: 22, color: TEXT_PRIMARY, fontWeight: '700' }} />
      ) : null}
      {subtitle ? (
        <TextWidget
          text={subtitle}
          style={{ fontSize: 11, color: TEXT_SECONDARY, marginTop: 4 }}
        />
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
        borderRadius: 16,
        padding: 16,
        flexDirection: 'column',
        justifyContent: 'center',
        alignItems: 'center',
      }}
    >
      <TextWidget
        text={props.message}
        maxLines={3}
        style={{ fontSize: 12, color: TEXT_SECONDARY, textAlign: 'center' }}
      />
    </FlexWidget>
  );
}
