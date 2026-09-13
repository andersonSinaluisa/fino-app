import { create } from 'zustand';
import type { TransactionDirection } from '../types/api';

/**
 * §22 ("Más detalles"): "NO perder información al cambiar de QuickEntry al
 * formulario completo."
 *
 * Ese requisito es la razón de que este store exista. El sheet y el formulario
 * completo son dos pantallas distintas (una es un modal sobre la pestaña actual, el
 * otro una ruta), así que lo que la persona ya escribió tiene que sobrevivir al
 * salto entre ambas. Pasarlo por parámetros de navegación funcionaría para el
 * monto, pero no para "vengo del sheet, y si cancelas quiero volver al sheet con lo
 * mismo puesto".
 *
 * También es lo que hace que el "+" sea un solo control global (§26: "no duplicar
 * FABs"): cualquier pantalla llama a `open()`, y el sheet vive una sola vez en el
 * layout de las pestañas.
 */

/** §38: por dónde entró el registro. Sin datos financieros, solo la vía. */
export type QuickEntrySource = 'home' | 'movements' | 'shortcut' | 'widget' | 'voice';

export interface QuickEntryDraft {
  /** Texto tal cual está en el campo de monto ("12.5", "5+2"), no un número. */
  amountText: string;
  direction: TransactionDirection;
  description: string;
  categoryId: string | null;
  /** ISO, o null para "ahora". */
  occurredAt: string | null;
  /** Etiqueta legible de la fecha ("Ayer"), solo para mostrar. */
  dateLabel: string | null;
  financialAccountId: string | null;
}

export const emptyDraft: QuickEntryDraft = {
  amountText: '',
  direction: 'Expense',
  description: '',
  categoryId: null,
  occurredAt: null,
  dateLabel: null,
  financialAccountId: null,
};

interface QuickEntryState {
  visible: boolean;
  source: QuickEntrySource;
  /**
   * Marca de tiempo de la apertura, para medir TIME_TO_CASH_ENTRY (§38). Se guarda
   * aquí y no en el componente porque el sheet puede remontarse (rotación, cambio
   * de pestaña) sin que la persona haya vuelto a empezar.
   */
  openedAt: number | null;
  /** True cuando se abre manteniendo pulsado el "+", para arrancar la voz sola. */
  startWithVoice: boolean;
  draft: QuickEntryDraft;
  /**
   * True mientras el formulario completo tiene el control. El sheet se oculta pero
   * NO se limpia: si la persona cancela el formulario, vuelve a encontrar lo suyo.
   */
  handedOffToFullForm: boolean;

  open: (source?: QuickEntrySource, options?: { withVoice?: boolean }) => void;
  close: () => void;
  setDraft: (patch: Partial<QuickEntryDraft>) => void;
  resetDraft: () => void;
  handOffToFullForm: () => void;
  returnFromFullForm: () => void;
  consumeVoiceIntent: () => void;
}

export const useQuickEntryStore = create<QuickEntryState>((set) => ({
  visible: false,
  source: 'home',
  openedAt: null,
  startWithVoice: false,
  draft: emptyDraft,
  handedOffToFullForm: false,

  open: (source = 'home', options) =>
    set((state) => ({
      visible: true,
      source,
      openedAt: Date.now(),
      startWithVoice: options?.withVoice ?? false,
      handedOffToFullForm: false,
      // Abrir de nuevo empieza de cero, SALVO que se venga de vuelta del
      // formulario completo -- ese caso lo maneja returnFromFullForm.
      draft: state.handedOffToFullForm ? state.draft : emptyDraft,
    })),

  close: () =>
    set({
      visible: false,
      openedAt: null,
      startWithVoice: false,
      handedOffToFullForm: false,
      draft: emptyDraft,
    }),

  setDraft: (patch) => set((state) => ({ draft: { ...state.draft, ...patch } })),

  resetDraft: () => set({ draft: emptyDraft }),

  handOffToFullForm: () => set({ visible: false, handedOffToFullForm: true }),

  returnFromFullForm: () => set({ visible: true, handedOffToFullForm: false }),

  consumeVoiceIntent: () => set({ startWithVoice: false }),
}));
