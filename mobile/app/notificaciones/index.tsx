import { useEffect, useState } from 'react';
import { Alert, FlatList, Pressable, StyleSheet, Switch, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Card, EmptyState, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { useDeviceStore } from '../../store/deviceStore';
import { useMarkNotificationRead, useNotifications, useUpdateNotificationPreferences } from '../../hooks/queries';
import { formatRelativeTime } from '../../utils/format';
import type { AppNotification, NotificationPreferences } from '../../types/api';

const CATEGORIES: Array<{
  key: 'notifyOnMovements' | 'notifyOnIncome' | 'notifyOnInsights' | 'notifyOnSecurity' | 'notifyOnReminders';
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  hint: string;
}> = [
  { key: 'notifyOnMovements', icon: 'cart-outline', label: 'Movimientos', hint: 'Compras y gastos detectados.' },
  { key: 'notifyOnIncome', icon: 'cash-outline', label: 'Ingresos', hint: 'Dinero recibido detectado.' },
  { key: 'notifyOnInsights', icon: 'bulb-outline', label: 'Insights', hint: 'Hallazgos nuevos sobre tus finanzas.' },
  {
    key: 'notifyOnSecurity',
    icon: 'shield-checkmark-outline',
    label: 'Seguridad',
    hint: 'Correos sospechosos u otra actividad de seguridad.',
  },
  {
    key: 'notifyOnReminders',
    icon: 'alarm-outline',
    label: 'Recordatorios',
    hint: 'Resumen periódico y cuentas desactualizadas.',
  },
];

/**
 * Entregable 17 ("Notificaciones"): historial de avisos + las cinco categorías
 * de preferencia que el backend gatilla de forma independiente (ver
 * NotificationDispatcher.ShouldNotify). No hay un GET para las preferencias de
 * este dispositivo por sí solas -- se siembran de lo último que devolvió el
 * registro o un cambio previo (useDeviceStore), y cada guardado envía el
 * objeto completo para no pisar el resto de campos.
 */
export default function NotificationsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();

  const expoPushToken = useDeviceStore((state) => state.expoPushToken);
  const storedPreferences = useDeviceStore((state) => state.preferences);
  const setStoredPreferences = useDeviceStore((state) => state.setPreferences);

  const { data: notifications, isLoading, refetch, isRefetching } = useNotifications();
  const markRead = useMarkNotificationRead();
  const updatePreferences = useUpdateNotificationPreferences(expoPushToken);

  const [preferences, setPreferences] = useState<NotificationPreferences | null>(storedPreferences);

  useEffect(() => {
    setPreferences(storedPreferences);
  }, [storedPreferences]);

  const togglePreference = (key: keyof NotificationPreferences) => {
    if (!preferences) {
      return;
    }

    const next: NotificationPreferences = { ...preferences, [key]: !preferences[key] };
    setPreferences(next);

    updatePreferences.mutate(next, {
      onSuccess: (saved) => {
        setStoredPreferences(saved);
      },
      onError: () => {
        // Revert: the UI must never claim a preference stuck when it didn't.
        setPreferences(preferences);
        Alert.alert('No pudimos guardar el cambio', 'Inténtalo de nuevo en un momento.');
      },
    });
  };

  return (
    <View style={[styles.root, { paddingTop: insets.top + spacing.sm }]}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cerrar
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Notificaciones</Typo>
      </View>

      <FlatList
        data={notifications ?? []}
        keyExtractor={(item) => item.id}
        contentContainerStyle={[styles.list, { paddingBottom: insets.bottom + 120 }]}
        showsVerticalScrollIndicator={false}
        refreshing={isRefetching}
        onRefresh={() => void refetch()}
        ListHeaderComponent={
          <View style={styles.section}>
            {preferences ? (
              <>
                <SectionHeader title="Este dispositivo" />
                <Card>
                  <CategoryRow
                    icon="notifications-outline"
                    label="Notificaciones push"
                    hint="Apágalas del todo en este dispositivo sin perder tus otras elecciones."
                    value={preferences.pushEnabled}
                    onValueChange={() => togglePreference('pushEnabled')}
                  />
                  <View style={styles.divider} />
                  <CategoryRow
                    icon="eye-off-outline"
                    label="Mostrar montos en el aviso"
                    hint="Si lo apagas, el aviso solo dice 'Abre Fino para ver el detalle.'"
                    value={preferences.showAmountsInPreview}
                    onValueChange={() => togglePreference('showAmountsInPreview')}
                  />
                </Card>

                <View style={styles.section}>
                  <SectionHeader title="Qué quieres recibir" />
                  <Card>
                    {CATEGORIES.map((category, index) => (
                      <View key={category.key}>
                        {index > 0 ? <View style={styles.divider} /> : null}
                        <CategoryRow
                          icon={category.icon}
                          label={category.label}
                          hint={category.hint}
                          value={preferences[category.key]}
                          onValueChange={() => togglePreference(category.key)}
                        />
                      </View>
                    ))}
                  </Card>
                </View>
              </>
            ) : (
              <Card>
                <Typo variant="caption" color={colors.textSecondary}>
                  {expoPushToken
                    ? 'Cargando tus preferencias...'
                    : 'Activa las notificaciones para elegir qué quieres recibir. En un simulador o sin permiso concedido, esto no está disponible.'}
                </Typo>
              </Card>
            )}

            <View style={styles.section}>
              <SectionHeader title="Historial" />
            </View>
          </View>
        }
        renderItem={({ item }) => <NotificationRow item={item} onPress={() => markRead.mutate(item.id)} />}
        ListEmptyComponent={
          isLoading ? (
            <View style={styles.loading}>
              <SkeletonCard />
              <SkeletonCard />
            </View>
          ) : (
            <EmptyState
              icon="notifications-outline"
              title="Sin notificaciones todavía"
              body="Cuando detectemos un movimiento o algo que valga la pena avisarte, aparecerá aquí."
            />
          )
        }
      />
    </View>
  );
}

function CategoryRow({
  icon,
  label,
  hint,
  value,
  onValueChange,
}: {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  hint: string;
  value: boolean;
  onValueChange: () => void;
}) {
  return (
    <View style={styles.row}>
      <View style={styles.rowIcon}>
        <Ionicons name={icon} size={17} color={colors.text} />
      </View>

      <View style={styles.rowBody}>
        <Typo variant="body">{label}</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {hint}
        </Typo>
      </View>

      <Switch value={value} onValueChange={onValueChange} />
    </View>
  );
}

function NotificationRow({ item, onPress }: { item: AppNotification; onPress: () => void }) {
  return (
    <Pressable onPress={item.isRead ? undefined : onPress} disabled={item.isRead}>
      <Card style={styles.notification}>
        <View style={styles.notificationTop}>
          {item.isRead ? null : <View style={styles.unreadDot} />}
          <View style={styles.flex}>
            <Typo variant="bodyStrong">{item.title}</Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              {item.body}
            </Typo>
          </View>
        </View>
        <Typo variant="overline" color={colors.textSecondary}>
          {formatRelativeTime(item.createdAt)}
        </Typo>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  flex: { flex: 1 },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginHorizontal: spacing.xl,
    marginBottom: spacing.lg,
  },
  header: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.lg,
    gap: 2,
  },
  section: {
    marginBottom: spacing.md,
    gap: spacing.sm,
  },
  list: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  loading: {
    gap: spacing.lg,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.sm,
  },
  rowIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  rowBody: {
    flex: 1,
    gap: 2,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.md,
  },
  notification: {
    gap: spacing.sm,
    marginBottom: spacing.md,
  },
  notificationTop: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  unreadDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.accent,
    marginTop: 6,
  },
});
