import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Redirect } from 'expo-router';
import { colors } from '../theme';
import { Typo } from '../components/ui/Typo';
import { useAuthStore } from '../store/authStore';

/** Decides where a cold start lands, once the stored session has been read. */
export default function Index() {
  const status = useAuthStore((state) => state.status);

  if (status === 'loading') {
    return (
      <View style={styles.splash}>
        <Typo variant="title">Nexo</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Todo tu dinero, en un solo lugar.
        </Typo>
        <ActivityIndicator color={colors.textSecondary} style={styles.spinner} />
      </View>
    );
  }

  return <Redirect href={status === 'authenticated' ? '/(tabs)' : '/(auth)/login'} />;
}

const styles = StyleSheet.create({
  splash: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    backgroundColor: colors.background,
  },
  spinner: {
    marginTop: 24,
  },
});
