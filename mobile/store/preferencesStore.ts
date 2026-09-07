import { create } from 'zustand';

interface PreferencesState {
  /** "Ocultar cantidades" on the home screen. Session-scoped by design. */
  amountsHidden: boolean;
  toggleAmounts: () => void;
  /**
   * "Ocultar montos en widgets" (home-screen widgets, iOS/Android): a
   * separate switch from `amountsHidden` because a widget sits on the home
   * screen where anyone glancing at the phone can see it, so someone may
   * want amounts hidden there even while they're fine seeing them inside
   * the unlocked app. Session-scoped by design, same as `amountsHidden` --
   * the widget snapshot is re-synced with the current value on every
   * change (see hooks/useWidgetSync.ts), so a widget never shows a stale
   * hidden/visible state for long.
   */
  hideAmountsInWidgets: boolean;
  toggleHideAmountsInWidgets: () => void;
}

export const usePreferencesStore = create<PreferencesState>((set) => ({
  amountsHidden: false,
  toggleAmounts: () => set((state) => ({ amountsHidden: !state.amountsHidden })),
  hideAmountsInWidgets: false,
  toggleHideAmountsInWidgets: () => set((state) => ({ hideAmountsInWidgets: !state.hideAmountsInWidgets })),
}));
