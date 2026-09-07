import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';

const HAS_SEEN_ONBOARDING_KEY = 'fino.hasSeenOnboarding';

interface OnboardingState {
  status: 'loading' | 'ready';
  /**
   * Whether this device has ever reached the onboarding screens -- not
   * whether the person finished connecting an account. Reaching it once is
   * enough for it to never show again, the same way a first-run tutorial
   * would work, so a returning anonymous user (e.g. right after logging
   * out) lands on the login screen instead of the pitch.
   */
  hasSeenOnboarding: boolean;
  restore: () => Promise<void>;
  markSeen: () => void;
}

export const useOnboardingStore = create<OnboardingState>((set) => ({
  status: 'loading',
  hasSeenOnboarding: false,

  restore: async () => {
    try {
      const stored = await SecureStore.getItemAsync(HAS_SEEN_ONBOARDING_KEY);
      set({ status: 'ready', hasSeenOnboarding: stored === '1' });
    } catch {
      set({ status: 'ready', hasSeenOnboarding: false });
    }
  },

  markSeen: () => {
    set({ hasSeenOnboarding: true });
    void SecureStore.setItemAsync(HAS_SEEN_ONBOARDING_KEY, '1').catch(() => undefined);
  },
}));
