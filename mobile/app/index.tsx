import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Redirect } from 'expo-router';
import { colors } from '../theme';
import { Typo } from '../components/ui/Typo';
import { useAuthStore } from '../store/authStore';
import { useOnboardingStore } from '../store/onboardingStore';

/** Decides where a cold start lands, once the stored session has been read. */
export default function Index() {
  const authStatus = useAuthStore((state) => state.status);
  const onboardingStatus = useOnboardingStore((state) => state.status);
  const hasSeenOnboarding = useOnboardingStore((state) => state.hasSeenOnboarding);

  if (authStatus === 'loading' || onboardingStatus === 'loading') {
    return (
      <View style={styles.splash}>
        <Typo variant="title">Fino</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Todo tu dinero, en un solo lugar.
        </Typo>
        <ActivityIndicator color={colors.textSecondary} style={styles.spinner} />
      </View>
    );
  }

  if (authStatus === 'authenticated') {
    return <Redirect href="/(tabs)" />;
  }

  // First-time anonymous visit goes through onboarding; a returning
  // anonymous user (already saw it once, e.g. right after logging out)
  // goes straight to login.
  return <Redirect href={hasSeenOnboarding ? '/(auth)/login' : '/(auth)/onboarding'} />;
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
