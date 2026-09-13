import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { useConnectivityStore } from '../../store/connectivityStore';
import { useAuthStore } from '../../store/authStore';
import { entriesForUser, useOfflineQueueStore } from '../../store/offlineQueueStore';
import { flushOfflineQueue } from '../../hooks/useOfflineSync';

/**
 * Modo offline, "registrar sin señal": lo único que le dice a la persona
 * que un movimiento suyo todavía no llegó al servidor. Deliberadamente NO
 * inventa números -- no toca el saldo ni el resumen, que siguen mostrando
 * exactamente lo que el servidor confirmó la última vez. Aparece en Inicio
 * mientras haya algo en la cola (services/offlineStorage.ts) y desaparece
 * solo cuando useOfflineSync termina de sincronizar todo.
 */
export function PendingSyncBanner() {
  const client = useQueryClient();
  const userId = useAuthStore((state) => state.user?.id);
  const allEntries = useOfflineQueueStore((state) => state.entries);
  // Solo lo de esta sesión -- ver el comentario de `userId` en
  // PendingQuickEntry (services/offlineStorage.ts). `allEntries` mantiene
  // identidad estable entre renders salvo que la cola de verdad cambie, así
  // que memoizar aquí evita recalcular el filtro en cada render sin motivo.
  const entries = useMemo(() => entriesForUser(allEntries, userId), [allEntries, userId]);
  const isOnline = useConnectivityStore((state) => state.isOnline);
  const [retrying, setRetrying] = useState(false);

  if (entries.length === 0) {
    return null;
  }

  // Solo la más reciente trae un error -- flushOfflineQueue se detiene en el
  // primer fallo, así que como mucho una entrada tiene lastError a la vez.
  const failed = entries.find((entry) => entry.lastError !== null);

  const label =
    entries.length === 1
      ? '1 movimiento pendiente de sincronizar'
      : `${entries.length} movimientos pendientes de sincronizar`;

  const handleRetry = async () => {
    setRetrying(true);
    try {
      await flushOfflineQueue(client);
    } finally {
      setRetrying(false);
    }
  };

  return (
    <View style={styles.banner}>
      <Ionicons
        name={failed ? 'alert-circle-outline' : 'cloud-upload-outline'}
        size={18}
        color={failed ? colors.warning : colors.textSecondary}
      />
      <View style={styles.textGroup}>
        <Typo variant="caption" color={colors.text}>
          {label}
        </Typo>
        {!isOnline ? (
          <Typo variant="caption" color={colors.textSecondary}>
            Se guardan en tu teléfono y se envían solos con señal.
          </Typo>
        ) : failed ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {failed.lastError}
          </Typo>
        ) : null}
      </View>
      {isOnline ? (
        <Pressable onPress={() => void handleRetry()} disabled={retrying} hitSlop={8} style={styles.retryButton}>
          {retrying ? (
            <ActivityIndicator size="small" color={colors.textSecondary} />
          ) : (
            <Typo variant="caption" color={colors.primary}>
              Reintentar
            </Typo>
          )}
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.lg,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    marginBottom: spacing.md,
  },
  textGroup: {
    flex: 1,
    gap: 2,
  },
  retryButton: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
});
