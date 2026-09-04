import { create } from 'zustand';

interface PreferencesState {
  /** "Ocultar cantidades" on the home screen. Session-scoped by design. */
  amountsHidden: boolean;
  toggleAmounts: () => void;
}

export const usePreferencesStore = create<PreferencesState>((set) => ({
  amountsHidden: false,
  toggleAmounts: () => set((state) => ({ amountsHidden: !state.amountsHidden })),
}));
