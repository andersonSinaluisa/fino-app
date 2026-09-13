import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';

const HAS_SEEN_ONBOARDING_KEY = 'fino.hasSeenOnboarding';
const FIRST_ACCOUNT_CARD_DISMISSED_KEY = 'fino.firstAccountCardDismissed';

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
  /**
   * Onboarding funcional: la tarjeta "Tu primera cuenta está lista ✓" en
   * Home se muestra una vez y desaparece para siempre una vez descartada --
   * a propósito solo local (SecureStore), no un quinto estado en el
   * backend, porque es una preferencia de qué ve ESTE dispositivo, no un
   * hito real del progreso de la persona.
   */
  firstAccountCardDismissed: boolean;
  restore: () => Promise<void>;
  markSeen: () => void;
  dismissFirstAccountCard: () => void;
}

export const useOnboardingStore = create<OnboardingState>((set) => ({
  status: 'loading',
  hasSeenOnboarding: false,
  firstAccountCardDismissed: false,

  restore: async () => {
    try {
      const [seen, cardDismissed] = await Promise.all([
        SecureStore.getItemAsync(HAS_SEEN_ONBOARDING_KEY),
        SecureStore.getItemAsync(FIRST_ACCOUNT_CARD_DISMISSED_KEY),
      ]);
      set({ status: 'ready', hasSeenOnboarding: seen === '1', firstAccountCardDismissed: cardDismissed === '1' });
    } catch {
      set({ status: 'ready', hasSeenOnboarding: false, firstAccountCardDismissed: false });
    }
  },

  markSeen: () => {
    set({ hasSeenOnboarding: true });
    void SecureStore.setItemAsync(HAS_SEEN_ONBOARDING_KEY, '1').catch(() => undefined);
  },

  dismissFirstAccountCard: () => {
    set({ firstAccountCardDismissed: true });
    void SecureStore.setItemAsync(FIRST_ACCOUNT_CARD_DISMISSED_KEY, '1').catch(() => undefined);
  },
}));
