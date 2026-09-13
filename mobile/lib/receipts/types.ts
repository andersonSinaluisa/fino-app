/**
 * §10: el modelo de lo que FINO logra sacar de una factura.
 *
 * Casi todo es opcional a propósito. El ÚNICO campo que de verdad bloquea la
 * creación de un movimiento es `total`: sin comercio se guarda igual, sin
 * fecha se usa hoy, sin IVA no pasa nada. Exigir una extracción perfecta
 * convertiría el escaneo en algo más lento que teclear el monto.
 */

/** §32: de dónde vino la imagen. Extensible sin tocar el resto del flujo. */
export const ReceiptSource = {
  Camera: 'camera',
  Gallery: 'gallery',
  /** Todavía no implementado; el tipo existe para que añadirlo no rompa nada. */
  Pdf: 'pdf',
} as const;

export type ReceiptSourceValue = (typeof ReceiptSource)[keyof typeof ReceiptSource];

export const PaymentHint = {
  Cash: 'cash',
  Card: 'card',
  Unknown: 'unknown',
} as const;

export type PaymentHintValue = (typeof PaymentHint)[keyof typeof PaymentHint];

/**
 * §33: preparado para FASE 4. En el MVP no se usa para crear movimientos --
 * una factura crea UN movimiento, no uno por producto.
 */
export interface ReceiptItem {
  name: string;
  quantity: number | null;
  unitPrice: number | null;
  total: number | null;
}

/**
 * §18: confianza por campo, 0..1.
 *
 * No se le enseña al usuario como porcentaje. Se usa para decidir qué campo
 * marcar con "toca para verificar" en la pantalla de revisión.
 */
export interface ReceiptFieldConfidence {
  total: number;
  date: number;
  merchant: number;
  tax: number;
}

export interface ReceiptExtractionResult {
  merchantName: string | null;
  /** Número de factura, cuando aparece. Nunca el RUC. */
  documentNumber: string | null;
  /** ISO (solo fecha). Null si no se reconoció una fecha razonable. */
  date: string | null;
  subtotal: number | null;
  tax: number | null;
  total: number | null;
  currency: string;
  paymentHint: PaymentHintValue;
  items: ReceiptItem[];
  /** Confianza global, la del total pesa la mayor parte. */
  confidence: number;
  fieldConfidence: ReceiptFieldConfidence;
  /**
   * §17: por qué el resultado no es de fiar, cuando no lo es. Códigos de un
   * conjunto cerrado -- se usan para el copy y para analytics, así que nunca
   * llevan texto del recibo.
   */
  warnings: ReceiptWarning[];
}

export const ReceiptWarning = {
  /** No se encontró ningún importe que parezca el total. */
  NoTotal: 'no_total',
  /** subtotal + IVA no cuadra con el total encontrado. */
  TotalsInconsistent: 'totals_inconsistent',
  /** Se vio "SUBTOTAL" pero ningún "TOTAL": puede que la foto corte la parte de abajo. */
  PossiblyCropped: 'possibly_cropped',
  /** El OCR devolvió muy poco texto. */
  LowText: 'low_text',
  /** La fecha leída no es razonable (futura o demasiado antigua) y se descartó. */
  UnreasonableDate: 'unreasonable_date',
} as const;

export type ReceiptWarning = (typeof ReceiptWarning)[keyof typeof ReceiptWarning];

/** §14: la UI nunca sabe qué motor hay debajo. */
export interface ReceiptExtractor {
  readonly name: string;
  extract(imageUri: string): Promise<ReceiptExtractionResult>;
}

export const EMPTY_FIELD_CONFIDENCE: ReceiptFieldConfidence = {
  total: 0,
  date: 0,
  merchant: 0,
  tax: 0,
};

export function emptyExtraction(warnings: ReceiptWarning[] = []): ReceiptExtractionResult {
  return {
    merchantName: null,
    documentNumber: null,
    date: null,
    subtotal: null,
    tax: null,
    total: null,
    currency: 'USD',
    paymentHint: PaymentHint.Unknown,
    items: [],
    confidence: 0,
    fieldConfidence: { ...EMPTY_FIELD_CONFIDENCE },
    warnings,
  };
}
