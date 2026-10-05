import { useEffect, useState } from 'react';
import { Alert, FlatList, Linking, Pressable, StyleSheet, Switch, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, EmptyState, SectionHeader, SelectSheet, SkeletonCard, Typo } from '../../components/ui';
import { api } from '../../services/endpoints';
import { registerForPushAsync } from '../../hooks/usePushRegistration';
import { AnalyticsSource } from '../../services/analytics';
import type { SelectSheetOption } from '../../components/ui';
import { useDeviceStore } from '../../store/deviceStore';
import { useMarkNotificationRead, useNotifications, useUpdateNotificationPreferences } from '../../hooks/queries';
import { formatRelativeTime } from '../../utils/format';
import { notificationRoute } from '../../utils/notificationRoute';
import type { AppNotification, NotificationPreferences } from '../../types/api';

const CATEGORIES: Array<{
  key:
    | 'notifyOnMovements'
    | 'notifyOnIncome'
    | 'notifyOnInsights'
    | 'notifyOnSecurity'
    | 'notifyOnReminders'
    | 'notifyOnPulses';
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
    hint: 'Pagos de tarjeta, estados nuevos, presupuestos, cuentas desactualizadas y resumen semanal.',
  },
  {
    key: 'notifyOnPulses',
    icon: 'pulse-outline',
    label: 'Pulso',
    hint: 'Novedades sobre tus finanzas detectadas automáticamente.',
  },
];

/** Horas 0-23 para el selector de horario "no molestar", en hora de Ecuador. */
const HOUR_OPTIONS: SelectSheetOption<string | undefined>[] = Array.from({ length: 24 }, (_, hour) => ({
  value: String(hour),
  label: `${String(hour).padStart(2, '0')}:00`,
}));

const DEFAULT_QUIET_START = 22;
const DEFAULT_QUIET_END = 7;

/**
 * Entregable 17 ("Notificaciones"): historial de avisos + las categorías
 * de preferencia que el backend gatilla de forma independiente (ver
 * NotificationDispatcher.ShouldNotify). No hay un GET para las preferencias de
 * este dispositivo por sí solas -- se siembran de lo último que devolvió el
 * registro o un cambio previo (useDeviceStore), y cada guardado envía el
 * objeto completo para no pisar el resto de campos. PULSO FASE 3 agrega la
 * categoría "Pulso" (arriba, vía CATEGORIES) y el horario "no molestar":
 * mientras está prendido, el backend sigue guardando cada notificación en el
 * historial -- solo silencia el push de ese dispositivo
 * (NotificationDispatcher.IsWithinQuietHours).
 */
export default function NotificationsScreen() {
  const [sendingTest, setSendingTest] = useState(false);
  const setDevice = useDeviceStore((state) => state.setDevice);
  const [enabling, setEnabling] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [enableMessage, setEnableMessage] = useState<string | null>(null);
  const enable = () => {
    if (blocked) {
      void Linking.openSettings();
      return;
    }

    setEnabling(true);
    void registerForPushAsync(setDevice, AnalyticsSource.Notification)
      .then((result) => {
        switch (result.status) {
          case 'registered':
            setEnableMessage(null);
            break;
          case 'blocked':
            setBlocked(true);
            setEnableMessage('Las notificaciones de Fino están apagadas en los Ajustes del teléfono. Ábrelos, entra a Fino → Notificaciones y actívalas.');
            break;
          case 'denied':
            setEnableMessage('No diste permiso. Puedes intentarlo de nuevo cuando quieras.');
            break;
          case 'unsupported':
            setEnableMessage('Las notificaciones no funcionan en un simulador ni en Expo Go. Prueba en un teléfono con la app instalada desde TestFlight o Play.');
            break;
          case 'error':
            setEnableMessage(`No pudimos registrar este dispositivo: ${result.message}`);
            break;
        }
      })
      .finally(() => setEnabling(false));
  };
  const sendTest = () => {
    setSendingTest(true);
    api.notifications
      .test()
      .then(({ devices, sent }) => {
        if (devices === 0) {
          Alert.alert('Sin dispositivos', 'Activa las notificaciones push en este dispositivo y vuelve a intentarlo.');
        } else if (sent === 0) {
          Alert.alert('No se pudo enviar', 'El servicio de notificaciones no aceptó el envío. Revisa los permisos de notificaciones del teléfono.');
        } else {
          Alert.alert('Enviada', 'Debería llegarte en unos segundos. Si no llega, revisa que las notificaciones de Fino estén permitidas en los ajustes del teléfono.');
        }
      })
      .catch(() => Alert.alert('No se pudo enviar', 'Inténtalo de nuevo en un momento.'))
      .finally(() => setSendingTest(false));
  };

  const router = useRouter();
  const insets = useSafeAreaInsets();

  const expoPushToken = useDeviceStore((state) => state.expoPushToken);
  const storedPreferences = useDeviceStore((state) => state.preferences);
  const setStoredPreferences = useDeviceStore((state) => state.setPreferences);

  const { data: notifications, isLoading, refetch, isRefetching } = useNotifications();
  const markRead = useMarkNotificationRead();
  const updatePreferences = useUpdateNotificationPreferences(expoPushToken);

  const [preferences, setPreferences] = useState<NotificationPreferences | null>(storedPreferences);
  const [activeHourField, setActiveHourField] = useState<'start' | 'end' | null>(null);

  useEffect(() => {
    setPreferences(storedPreferences);
  }, [storedPreferences]);

  const savePreferences = (next: NotificationPreferences) => {
    const previous = preferences;
    setPreferences(next);

    updatePreferences.mutate(next, {
      onSuccess: (saved) => {
        setStoredPreferences(saved);
      },
      onError: () => {
        // Revert: the UI must never claim a preference stuck when it didn't.
        setPreferences(previous);
        Alert.alert('No pudimos guardar el cambio', 'Inténtalo de nuevo en un momento.');
      },
    });
  };

  const togglePreference = (key: keyof NotificationPreferences) => {
    if (!preferences) {
      return;
    }

    savePreferences({ ...preferences, [key]: !preferences[key] });
  };

  const toggleQuietHours = (enabled: boolean) => {
    if (!preferences) {
      return;
    }

    savePreferences({
      ...preferences,
      quietHoursStartHour: enabled ? DEFAULT_QUIET_START : null,
      quietHoursEndHour: enabled ? DEFAULT_QUIET_END : null,
    });
  };

  const setQuietHour = (field: 'start' | 'end', hour: string | undefined) => {
    if (!preferences || hour === undefined) {
      return;
    }

    savePreferences({
      ...preferences,
      quietHoursStartHour: field === 'start' ? Number(hour) : preferences.quietHoursStartHour,
      quietHoursEndHour: field === 'end' ? Number(hour) : preferences.quietHoursEndHour,
    });
  };

  const quietHoursEnabled = preferences?.quietHoursStartHour !== null && preferences?.quietHoursEndHour !== null;

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
                <View style={styles.testButton}>
                  <Button
                    label="Enviar notificación de prueba"
                    variant="secondary"
                    loading={sendingTest}
                    onPress={sendTest}
                  />
                </View>

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

                <View style={styles.section}>
                  <SectionHeader title="Horario sin avisos" />
                  <Card>
                    <CategoryRow
                      icon="moon-outline"
                      label="No molestar"
                      hint="El push se silencia en este horario; el historial se sigue guardando igual."
                      value={quietHoursEnabled}
                      onValueChange={() => toggleQuietHours(!quietHoursEnabled)}
                    />
                    {quietHoursEnabled ? (
                      <>
                        <View style={styles.divider} />
                        <View style={styles.hourRow}>
                          <Pressable
                            style={styles.hourButton}
                            onPress={() => setActiveHourField('start')}
                            accessibilityRole="button"
                          >
                            <Typo variant="caption" color={colors.textSecondary}>
                              Desde
                            </Typo>
                            <Typo variant="body">
                              {String(preferences.quietHoursStartHour).padStart(2, '0')}:00
                            </Typo>
                          </Pressable>
                          <Pressable
                            style={styles.hourButton}
                            onPress={() => setActiveHourField('end')}
                            accessibilityRole="button"
                          >
                            <Typo variant="caption" color={colors.textSecondary}>
                              Hasta
                            </Typo>
                            <Typo variant="body">
                              {String(preferences.quietHoursEndHour).padStart(2, '0')}:00
                            </Typo>
                          </Pressable>
                        </View>
                      </>
                    ) : null}
                  </Card>
                </View>
              </>
            ) : (
              <Card style={styles.enableCard}>
                {expoPushToken ? (
                  <Typo variant="caption" color={colors.textSecondary}>
                    Cargando tus preferencias...
                  </Typo>
                ) : (
                  <>
                    <Typo variant="bodyStrong">Notificaciones desactivadas</Typo>
                    <Typo variant="caption" color={colors.textSecondary}>
                      {enableMessage ??
                        'Actívalas para saber cuando detectemos un gasto o un ingreso.'}
                    </Typo>
                    <Button
                      label={blocked ? 'Abrir Ajustes del teléfono' : 'Activar notificaciones'}
                      loading={enabling}
                      onPress={enable}
                    />
                  </>
                )}
              </Card>
            )}

            <View style={styles.section}>
              <SectionHeader title="Historial" />
            </View>
          </View>
        }
        renderItem={({ item }) => (
          <NotificationRow
            item={item}
            onPress={() => {
              if (!item.isRead) {
                markRead.mutate(item.id);
              }
              const target = notificationRoute(item.data);
              if (target) {
                router.push(target);
              }
            }}
          />
        )}
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

      <SelectSheet
        visible={activeHourField !== null}
        title={activeHourField === 'start' ? 'Desde qué hora' : 'Hasta qué hora'}
        options={HOUR_OPTIONS}
        selectedValue={
          activeHourField === 'start'
            ? preferences?.quietHoursStartHour?.toString()
            : preferences?.quietHoursEndHour?.toString()
        }
        onSelect={(value) => {
          if (activeHourField) {
            setQuietHour(activeHourField, value);
          }
          setActiveHourField(null);
        }}
        onClose={() => setActiveHourField(null)}
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

const TYPE_ICONS: Record<string, keyof typeof Ionicons.glyphMap> = {
  ExpenseDetected: 'cart-outline',
  IncomeDetected: 'cash-outline',
  ImportCompleted: 'document-text-outline',
  InsightReady: 'bulb-outline',
  WeeklySummary: 'stats-chart-outline',
  AccountNeedsUpdate: 'refresh-outline',
  SecurityAlert: 'shield-checkmark-outline',
  PulseReady: 'pulse-outline',
  CardPaymentDue: 'card-outline',
  StatementAvailable: 'receipt-outline',
  BudgetThreshold: 'pie-chart-outline',
};

function NotificationRow({ item, onPress }: { item: AppNotification; onPress: () => void }) {
  const opensSomething = notificationRoute(item.data) !== null;
  return (
    <Pressable onPress={onPress} disabled={item.isRead && !opensSomething}>
      <Card style={styles.notification}>
        <View style={styles.notificationTop}>
          <View style={styles.typeIcon}>
            <Ionicons name={TYPE_ICONS[item.type] ?? 'notifications-outline'} size={16} color={colors.text} />
            {item.isRead ? null : <View style={styles.unreadBadge} />}
          </View>
          <View style={styles.flex}>
            <Typo variant="bodyStrong">{item.title}</Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              {item.body}
            </Typo>
          </View>
          {opensSomething ? <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} /> : null}
        </View>
        <Typo variant="overline" color={colors.textSecondary}>
          {formatRelativeTime(item.createdAt)}
        </Typo>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  enableCard: {
    gap: spacing.md,
  },
  testButton: {
    marginTop: spacing.md,
  },
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
  hourRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  hourButton: {
    flex: 1,
    gap: 2,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
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
  typeIcon: {
    width: 32,
    height: 32,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  unreadBadge: {
    position: 'absolute',
    top: -2,
    right: -2,
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: colors.accent,
    borderWidth: 2,
    borderColor: colors.surface,
  },
});
