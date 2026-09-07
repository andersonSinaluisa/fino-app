import { useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, SkeletonCard, Typo } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { useRevokeSession, useSessions } from '../../hooks/queries';
import { formatRelativeTime } from '../../utils/format';
import type { Session } from '../../types/api';

/**
 * Entregable 19 ("Sesiones y dispositivos"): a refresh token is single-use and
 * rotates every time it is used, so "sessions" here is exactly the set of
 * currently active refresh tokens for this user -- one per device, since a
 * device only ever rotates its own. `createdAt` is that row's own issuance
 * time, so for a session quietly rotating in the background it reads as
 * "última actividad", never "iniciaste sesión el...".
 */
export default function SessionsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();

  const { data: sessions, isLoading, refetch, isRefetching } = useSessions();
  const revokeSession = useRevokeSession();
  const logoutAllDevices = useAuthStore((state) => state.logoutAllDevices);
  const [signingOutEverywhere, setSigningOutEverywhere] = useState(false);

  const confirmRevoke = (session: Session) => {
    Alert.alert(
      'Cerrar esta sesión',
      `${deviceLabelOf(session)} dejará de tener acceso a tu cuenta hasta que vuelva a iniciar sesión.`,
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Cerrar sesión',
          style: 'destructive',
          onPress: () =>
            revokeSession.mutate(session.id, {
              onError: () =>
                Alert.alert('No pudimos cerrar esa sesión', 'Inténtalo de nuevo en un momento.'),
            }),
        },
      ],
    );
  };

  const confirmLogoutEverywhere = () => {
    Alert.alert(
      'Cerrar sesión en todos los dispositivos',
      'Esto incluye este dispositivo -- tendrás que volver a iniciar sesión aquí también.',
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Cerrar todo',
          style: 'destructive',
          onPress: () => {
            setSigningOutEverywhere(true);
            logoutAllDevices()
              .then(() => router.replace('/(auth)/login'))
              .catch(() => {
                setSigningOutEverywhere(false);
                Alert.alert('No pudimos completar la operación', 'Inténtalo de nuevo en un momento.');
              });
          },
        },
      ],
    );
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
        <Typo variant="title">Sesiones</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Dispositivos con acceso a tu cuenta ahora mismo.
        </Typo>
      </View>

      <FlatList
        data={sessions ?? []}
        keyExtractor={(item) => item.id}
        contentContainerStyle={[styles.list, { paddingBottom: insets.bottom + 140 }]}
        showsVerticalScrollIndicator={false}
        refreshing={isRefetching}
        onRefresh={() => void refetch()}
        renderItem={({ item }) => <SessionRow session={item} onRevoke={() => confirmRevoke(item)} />}
        ListEmptyComponent={
          isLoading ? (
            <View style={styles.loading}>
              <SkeletonCard />
              <SkeletonCard />
            </View>
          ) : (
            <EmptyState icon="phone-portrait-outline" title="Sin sesiones activas" />
          )
        }
        ListFooterComponent={
          sessions && sessions.length > 0 ? (
            <View style={styles.footer}>
              {signingOutEverywhere ? (
                <ActivityIndicator color={colors.textSecondary} />
              ) : (
                <Button
                  label="Cerrar sesión en todos los dispositivos"
                  variant="ghost"
                  onPress={confirmLogoutEverywhere}
                />
              )}
            </View>
          ) : null
        }
      />
    </View>
  );
}

function deviceLabelOf(session: Session): string {
  return session.deviceLabel ?? session.userAgent ?? 'Este dispositivo';
}

function SessionRow({ session, onRevoke }: { session: Session; onRevoke: () => void }) {
  return (
    <Card style={styles.session}>
      <View style={styles.sessionTop}>
        <View style={styles.sessionIcon}>
          <Ionicons name="phone-portrait-outline" size={17} color={colors.text} />
        </View>

        <View style={styles.flex}>
          <View style={styles.sessionTitleRow}>
            <Typo variant="bodyStrong" numberOfLines={1}>
              {deviceLabelOf(session)}
            </Typo>
            {session.isCurrent ? <Badge label="Este dispositivo" tone="accent" /> : null}
          </View>
          <Typo variant="caption" color={colors.textSecondary}>
            Última actividad: {formatRelativeTime(session.createdAt)}
          </Typo>
        </View>
      </View>

      {session.isCurrent ? null : (
        <Pressable onPress={onRevoke} style={styles.revokeAction} hitSlop={8}>
          <Typo variant="caption" color={colors.danger}>
            Cerrar esta sesión
          </Typo>
        </Pressable>
      )}
    </Card>
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
  list: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  loading: {
    gap: spacing.lg,
  },
  session: {
    gap: spacing.sm,
    marginBottom: spacing.md,
  },
  sessionTop: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  sessionIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  sessionTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  revokeAction: {
    alignSelf: 'flex-start',
    borderRadius: radius.md,
  },
  footer: {
    marginTop: spacing.lg,
    alignItems: 'center',
  },
});
