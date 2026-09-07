import React, { useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import type { WidgetConfigurationScreenProps } from 'react-native-android-widget';
import { colors, typography } from '../../../theme';
import { ANDROID_WIDGET_NAMES, type AndroidWidgetName } from '../constants';
import { readSnapshotFile, writeSelection } from '../storage';
import { emptySnapshot, type SelectableAccount, type SelectableCategory, type WidgetSnapshot } from '../types';
import { renderAndroidWidget } from './widgets';

/**
 * "Cuenta" and "Presupuesto" are the two configurable widgets (per-instance
 * picker, real react-native-android-widget feature -- see the `widgetFeatures:
 * 'reconfigurable|configuration_optional'` entries in app.json). Android
 * launches this as a normal screen (registered via
 * `registerWidgetConfigurationScreen` in index.js) when a widget is first
 * added, or later via "hold widget > Configure". It reads the same snapshot
 * file the headless task handler reads (see storage.ts) rather than the
 * app's live React Query cache, since this screen can be launched
 * independently of the app's own navigation stack.
 */
export function FinoWidgetConfigurationScreen({
  widgetInfo,
  renderWidget,
  setResult,
}: WidgetConfigurationScreenProps): React.JSX.Element {
  const [snapshot, setSnapshot] = useState<WidgetSnapshot | null>(null);

  useEffect(() => {
    let cancelled = false;
    readSnapshotFile()
      .then((raw) => {
        if (cancelled) return;
        if (!raw) {
          setSnapshot(emptySnapshot());
          return;
        }
        try {
          setSnapshot(JSON.parse(raw) as WidgetSnapshot);
        } catch {
          setSnapshot(emptySnapshot());
        }
      })
      .catch(() => {
        if (!cancelled) setSnapshot(emptySnapshot());
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const widgetName = widgetInfo.widgetName as AndroidWidgetName;
  const isCategory = widgetName === ANDROID_WIDGET_NAMES.categorySpend;

  if (!snapshot) {
    return (
      <View style={[styles.container, styles.centered]}>
        <ActivityIndicator color={colors.textSecondary} />
      </View>
    );
  }

  const options: Array<SelectableCategory | SelectableAccount> = isCategory
    ? snapshot.selectableCategories
    : snapshot.selectableAccounts;

  function choose(id: string) {
    if (!snapshot) return;
    void writeSelection(widgetInfo.widgetId, id);
    renderWidget(renderAndroidWidget(widgetName, snapshot, id));
    setResult('ok');
  }

  if (!snapshot.isAuthenticated) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Inicia sesión en Fino primero</Text>
        <Pressable style={styles.cancelButton} onPress={() => setResult('cancel')}>
          <Text style={styles.cancelText}>Cancelar</Text>
        </Pressable>
      </View>
    );
  }

  if (options.length === 0) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>
          {isCategory ? 'Todavía no tienes gastos categorizados este mes.' : 'Todavía no tienes cuentas agregadas.'}
        </Text>
        <Pressable style={styles.cancelButton} onPress={() => setResult('cancel')}>
          <Text style={styles.cancelText}>Cancelar</Text>
        </Pressable>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>{isCategory ? 'Elige una categoría' : 'Elige una cuenta'}</Text>
      <FlatList
        data={options}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <Pressable style={styles.option} onPress={() => choose(item.id)}>
            <Text style={styles.optionText}>{isCategory ? (item as SelectableCategory).name : (item as SelectableAccount).alias}</Text>
          </Pressable>
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
    padding: 20,
    paddingTop: 48,
  },
  centered: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: {
    ...typography.title,
    color: colors.text,
    marginBottom: 16,
  },
  option: {
    paddingVertical: 14,
    paddingHorizontal: 16,
    borderRadius: 12,
    backgroundColor: colors.surface,
    marginBottom: 8,
  },
  optionText: {
    ...typography.body,
    color: colors.text,
  },
  cancelButton: {
    marginTop: 24,
    alignSelf: 'flex-start',
  },
  cancelText: {
    ...typography.body,
    color: colors.textSecondary,
  },
});
