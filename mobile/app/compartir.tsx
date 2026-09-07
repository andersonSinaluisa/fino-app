import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { Redirect, useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../theme';
import { EmptyState, Screen, SkeletonCard, Typo } from '../components/ui';
import { AccountCard } from '../components/accounts/AccountCard';
import { useAccounts } from '../hooks/queries';
import { useAuthStore } from '../store/authStore';
import { clearPendingSharedFile, readPendingSharedFile } from '../lib/shareImport';
import type { PendingSharedFile } from '../lib/shareImport.types';
import type { Account } from '../types/api';

type Status = 'loading' | 'ready' | 'missing';

/**
 * Deep-link target for fino:///compartir -- opened by the iOS share
 * extension (targets/share/ShareViewController.swift) and by Android's
 * ACTION_SEND handling in MainActivity (plugins/withShareIntent.js) when
 * the person shares a bank statement into Fino from another app instead of
 * picking it by hand from cuentas/importar.tsx. All this screen does is
 * pick up the file both native sides already staged and ask which account
 * it belongs to -- the upload/preview/import flow itself is entirely
 * app/cuentas/importar.tsx's, reused unchanged via its sharedUri/sharedName
 * params.
 */
export default function CompartirScreen() {
  const router = useRouter();
  const { file } = useLocalSearchParams<{ file?: string }>();
  const authStatus = useAuthStore((state) => state.status);
  const { data: accounts, isLoading: accountsLoading } = useAccounts();

  const [status, setStatus] = useState<Status>('loading');
  const [shared, setShared] = useState<PendingSharedFile | null>(null);

  useEffect(() => {
    if (authStatus !== 'authenticated') {
      return;
    }
    let cancelled = false;
    void readPendingSharedFile(file).then((result) => {
      if (cancelled) {
        return;
      }
      setShared(result);
      setStatus(result ? 'ready' : 'missing');
    });
    return () => {
      cancelled = true;
    };
  }, [authStatus, file]);

  if (authStatus === 'loading') {
    return (
      <View style={styles.centered}>
        <ActivityIndicator color={colors.textSecondary} />
      </View>
    );
  }

  // Sharing into Fino while signed out has nowhere sensible to land -- the
  // shared file stays staged natively (App Group / share-inbox) and is
  // still there once the person signs in and shares again, so nothing is
  // lost by bouncing to login instead of trying to thread accountId through
  // the auth flow for a one-off file.
  if (authStatus !== 'authenticated') {
    return <Redirect href="/(auth)/login" />;
  }

  const selectAccount = (account: Account) => {
    if (!shared) {
      return;
    }
    clearPendingSharedFile();
    router.replace({
      pathname: '/cuentas/importar',
      params: { accountId: account.id, sharedUri: shared.uri, sharedName: shared.name },
    });
  };

  return (
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cerrar
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">¿A qué cuenta pertenece?</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          {shared ? shared.name : 'Compartiste un archivo con Fino.'}
        </Typo>
      </View>

      {status === 'missing' ? (
        <EmptyState
          icon="alert-circle-outline"
          title="No encontramos el archivo"
          body="Vuelve a compartirlo desde la app de tu banco."
          actionLabel="Cerrar"
          onAction={() => router.back()}
        />
      ) : status === 'loading' || accountsLoading ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : !accounts || accounts.length === 0 ? (
        <EmptyState
          icon="wallet-outline"
          title="Aún no tienes cuentas"
          body="Agrega una cuenta antes de importar un estado de cuenta."
          actionLabel="Agregar cuenta"
          onAction={() => router.replace('/cuentas/agregar')}
        />
      ) : (
        <View style={styles.stack}>
          {accounts.map((account) => (
            <AccountCard key={account.id} account={account} compact onPress={selectAccount} />
          ))}
        </View>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  header: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  stack: {
    gap: spacing.md,
  },
  centered: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.background,
  },
});
