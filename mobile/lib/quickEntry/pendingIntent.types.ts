/** Lo que dejó un App Intent de iOS antes de traer Fino al frente. */
export interface PendingQuickEntryIntent {
  /** "voice" abre el sheet ya escuchando; "keypad" lo abre normal. */
  mode: 'keypad' | 'voice';
}
