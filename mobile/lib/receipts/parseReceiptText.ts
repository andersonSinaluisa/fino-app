import {
  PaymentHint,
  ReceiptWarning,
  emptyExtraction,
  type PaymentHintValue,
  type ReceiptExtractionResult,
} from './types';

/**
 * §12-13: el parser de facturas ecuatorianas.
 *
 * Todo el trabajo real del escaneo vive aquí, y es determinístico: entra el
 * texto que devolvió el OCR, sale la estructura. Sin red, sin IA, sin estado.
 * Por eso se puede probar de verdad (ver __tests__/receipts/).
 *
 * EL PROBLEMA CENTRAL es no confundir el total con otra cifra. Un recibo
 * ecuatoriano típico termina así:
 *
 *     SUBTOTAL      20.00
 *     IVA 15%        3.00
 *     TOTAL         23.00
 *     EFECTIVO      25.00
 *     CAMBIO         2.00
 *
 * El gasto es 23.00. Un parser ingenuo que busque "el número más grande"
 * devuelve 25.00, y uno que busque la última cifra devuelve 2.00. Por eso
 * aquí se clasifica CADA línea por su etiqueta, las etiquetas excluyentes se
 * miran ANTES que las de total, y al final se comprueba la coherencia
 * aritmética.
 */

/** Etiquetas que NUNCA son el total, por mucho que lleven un importe al lado. */
const EXCLUDED_LABELS: readonly RegExp[] = [
  /\bSUB\s*[-.]?\s*TOTAL\b/,
  /\bBASE\s+IMPONIBLE\b/,
  /\bBASE\s+GRAVADA\b/,
  /\bI\.?V\.?A\.?\b/,
  /\bIMPUESTOS?\b/,
  /\bICE\b/,
  /\bCAMBIO\b/,
  /\bSU\s+CAMBIO\b/,
  /\bVUELTO\b/,
  /\bEFECTIVO\b/,
  /\bRECIBIDO\b/,
  /\bTARJETA\b/,
  /\bVISA\b/,
  /\bMASTERCARD\b/,
  /\bDEBITO\b/,
  /\bCREDITO\b/,
  /\bPROPINA\b/,
  /\bSERVICIO\b/,
  /\bDESCUENTO\b/,
  /\bAHORRO\b/,
  /\bCANT\b/,
  /\bP\.?\s*UNIT/,
];

/**
 * Etiquetas de total, de más específica a menos. El peso se usa para elegir
 * cuando aparece más de una: "TOTAL A PAGAR" gana a un "TOTAL" suelto, que es
 * justo el caso 4 del spec.
 */
const TOTAL_LABELS: readonly { pattern: RegExp; weight: number }[] = [
  { pattern: /\bTOTAL\s+A\s+PAGAR\b/, weight: 1 },
  { pattern: /\bVALOR\s+TOTAL\b/, weight: 1 },
  { pattern: /\bIMPORTE\s+TOTAL\b/, weight: 1 },
  { pattern: /\bTOTAL\s+GENERAL\b/, weight: 1 },
  { pattern: /\bNETO\s+A\s+PAGAR\b/, weight: 0.98 },
  { pattern: /\bTOTAL\b/, weight: 0.9 },
];

const SUBTOTAL_LABEL = /\bSUB\s*[-.]?\s*TOTAL\b/;
const TAX_LABEL = /\bI\.?V\.?A\.?\b/;

const CASH_LABEL = /\bEFECTIVO\b/;
const CARD_LABEL = /\b(TARJETA|VISA|MASTERCARD|DINERS|DISCOVER|DEBITO|CREDITO|D[EÉ]BITO|CR[EÉ]DITO)\b/;

/** Líneas de cabecera que no son el nombre del comercio. */
const NOT_MERCHANT = /\b(RUC|R\.U\.C|FACTURA|NOTA\s+DE\s+VENTA|COMPROBANTE|AUTORIZACI[OÓ]N|N[UÚ]MERO|SRI|AMBIENTE|EMISI[OÓ]N|CLIENTE|DIRECCI[OÓ]N|TEL[EÉ]FONO|FECHA|CONSUMIDOR\s+FINAL)\b/;

const DOCUMENT_NUMBER = /\b(\d{3}-\d{3}-\d{6,9})\b/;

/**
 * Un importe: 1.234,56 / 1,234.56 / 24.58 / 24,58 / 100.
 * Se exige al menos un dígito; los decimales son opcionales porque hay
 * recibos que escriben "TOTAL 10".
 */
const AMOUNT = /(?:USD|\$)?\s*(\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s*$/;

interface LabelledAmount {
  label: string;
  amount: number;
  lineIndex: number;
}

export interface ParseOptions {
  /** Inyectable para tests deterministas. */
  now?: Date;
}

export function parseReceiptText(rawText: string, options: ParseOptions = {}): ReceiptExtractionResult {
  const now = options.now ?? new Date();
  const lines = normalizeLines(rawText);

  if (lines.length === 0) {
    return emptyExtraction([ReceiptWarning.LowText, ReceiptWarning.NoTotal]);
  }

  const warnings: ReceiptWarning[] = [];

  if (lines.join('').length < 20) {
    warnings.push(ReceiptWarning.LowText);
  }

  const labelled = collectLabelledAmounts(lines);

  const total = pickTotal(labelled);
  const subtotal = pickByLabel(labelled, SUBTOTAL_LABEL);
  const tax = pickTax(labelled);

  const date = pickDate(lines, now);
  if (date === null && hasDateLikeToken(lines)) {
    warnings.push(ReceiptWarning.UnreasonableDate);
  }

  const merchant = pickMerchant(lines);
  const paymentHint = pickPaymentHint(lines);

  // §17: coherencia aritmética. No corrige el total -- avisa. Corregirlo en
  // silencio sería exactamente "confiar ciegamente en el OCR" al revés.
  let totalsConsistent: boolean | null = null;
  if (total !== null && subtotal !== null && tax !== null) {
    totalsConsistent = Math.abs(subtotal + tax - total) <= 0.02;
    if (!totalsConsistent) {
      warnings.push(ReceiptWarning.TotalsInconsistent);
    }
  }

  if (total === null) {
    warnings.push(ReceiptWarning.NoTotal);

    // §31: se vio el subtotal pero no el total -- casi siempre la foto corta
    // la parte de abajo del recibo, que es donde está el total.
    if (subtotal !== null) {
      warnings.push(ReceiptWarning.PossiblyCropped);
    }
  }

  const totalConfidence = confidenceForTotal(total, labelled, totalsConsistent);

  return {
    merchantName: merchant,
    documentNumber: pickDocumentNumber(lines),
    date,
    subtotal,
    tax,
    total,
    currency: 'USD',
    paymentHint,
    items: [],
    confidence: totalConfidence,
    fieldConfidence: {
      total: totalConfidence,
      date: date === null ? 0 : 0.85,
      merchant: merchant === null ? 0 : 0.7,
      tax: tax === null ? 0 : 0.9,
    },
    warnings,
  };
}

// --- líneas e importes -------------------------------------------------------

function normalizeLines(rawText: string): string[] {
  return rawText
    .split(/\r?\n/)
    .map((line) => line.replace(/\s+/g, ' ').trim().toUpperCase())
    .filter((line) => line.length > 0);
}

function collectLabelledAmounts(lines: string[]): LabelledAmount[] {
  const found: LabelledAmount[] = [];

  lines.forEach((line, lineIndex) => {
    const match = AMOUNT.exec(line);
    if (!match) {
      return;
    }

    const amount = parseAmount(match[1]!);
    if (amount === null) {
      return;
    }

    const label = line.slice(0, match.index).trim();
    found.push({ label, amount, lineIndex });
  });

  return found;
}

/**
 * "1.234,56" y "1,234.56" significan lo mismo; "24,58" y "24.58" también.
 * Regla: el ÚLTIMO separador es el decimal si deja 1 o 2 dígitos detrás.
 */
export function parseAmount(raw: string): number | null {
  const cleaned = raw.replace(/[^\d.,]/g, '');
  if (cleaned.length === 0) {
    return null;
  }

  const lastDot = cleaned.lastIndexOf('.');
  const lastComma = cleaned.lastIndexOf(',');
  const lastSeparator = Math.max(lastDot, lastComma);

  let normalized: string;

  if (lastSeparator === -1) {
    normalized = cleaned;
  } else {
    const decimals = cleaned.length - lastSeparator - 1;

    if (decimals === 1 || decimals === 2) {
      normalized = `${cleaned.slice(0, lastSeparator).replace(/[.,]/g, '')}.${cleaned.slice(lastSeparator + 1)}`;
    } else {
      // Separador de miles: "1.234" -> 1234
      normalized = cleaned.replace(/[.,]/g, '');
    }
  }

  const value = Number.parseFloat(normalized);
  return Number.isFinite(value) ? value : null;
}

function isExcluded(label: string): boolean {
  return EXCLUDED_LABELS.some((pattern) => pattern.test(label));
}

/**
 * §13: elige el total.
 *
 * El orden importa: primero se descarta todo lo que lleve una etiqueta
 * excluyente (SUBTOTAL, IVA, EFECTIVO, CAMBIO...), y solo sobre lo que queda
 * se buscan las etiquetas de total. Así "EFECTIVO 25.00" no puede ganar por
 * ser el número más grande, y "SUBTOTAL 20.00" no cuela por contener la
 * palabra TOTAL.
 */
function pickTotal(labelled: LabelledAmount[]): number | null {
  let best: { amount: number; weight: number; lineIndex: number } | null = null;

  for (const entry of labelled) {
    if (isExcluded(entry.label)) {
      continue;
    }

    for (const { pattern, weight } of TOTAL_LABELS) {
      if (!pattern.test(entry.label)) {
        continue;
      }

      // Empate de peso: gana la línea más abajo, que en un recibo es la
      // definitiva.
      if (best === null || weight > best.weight || (weight === best.weight && entry.lineIndex > best.lineIndex)) {
        best = { amount: entry.amount, weight, lineIndex: entry.lineIndex };
      }

      break;
    }
  }

  if (best === null || best.amount <= 0 || !Number.isFinite(best.amount)) {
    return null;
  }

  return round2(best.amount);
}

function pickByLabel(labelled: LabelledAmount[], pattern: RegExp): number | null {
  const matches = labelled.filter((entry) => pattern.test(entry.label));
  const last = matches[matches.length - 1];
  return last && last.amount > 0 ? round2(last.amount) : null;
}

/** El IVA puede venir como "IVA", "IVA 15%", "IVA 12%" o "I.V.A.". */
function pickTax(labelled: LabelledAmount[]): number | null {
  const matches = labelled.filter((entry) => TAX_LABEL.test(entry.label) && !SUBTOTAL_LABEL.test(entry.label));

  // "IVA 0%" existe y su importe es 0: es un valor válido, no un fallo.
  const last = matches[matches.length - 1];
  return last ? round2(last.amount) : null;
}

function confidenceForTotal(
  total: number | null,
  labelled: LabelledAmount[],
  totalsConsistent: boolean | null,
): number {
  if (total === null) {
    return 0;
  }

  let confidence = 0.8;

  // Una etiqueta explícita e inequívoca sube la confianza.
  const explicit = labelled.some(
    (entry) =>
      !isExcluded(entry.label) &&
      /\b(TOTAL\s+A\s+PAGAR|VALOR\s+TOTAL|IMPORTE\s+TOTAL|TOTAL\s+GENERAL)\b/.test(entry.label),
  );

  if (explicit) {
    confidence += 0.1;
  }

  if (totalsConsistent === true) {
    confidence += 0.1;
  } else if (totalsConsistent === false) {
    confidence -= 0.35;
  }

  return Math.max(0, Math.min(1, round2(confidence)));
}

// --- fecha -------------------------------------------------------------------

const MONTHS: Record<string, number> = {
  ENE: 1, FEB: 2, MAR: 3, ABR: 4, MAY: 5, JUN: 6,
  JUL: 7, AGO: 8, SEP: 9, SET: 9, OCT: 10, NOV: 11, DIC: 12,
};

const NUMERIC_DATE = /\b(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{2,4})\b/;
const ISO_DATE = /\b(\d{4})-(\d{2})-(\d{2})\b/;
const TEXT_DATE = /\b(\d{1,2})\s*[-/ ]\s*([A-Z]{3})[A-Z]*\s*[-/ ]\s*(\d{2,4})\b/;

function hasDateLikeToken(lines: string[]): boolean {
  return lines.some((line) => NUMERIC_DATE.test(line) || ISO_DATE.test(line) || TEXT_DATE.test(line));
}

/**
 * §17: una fecha "razonable". Se rechaza el futuro (más allá de hoy) y
 * cualquier cosa anterior al año 2000: casi siempre es el OCR leyendo mal el
 * número de autorización o el RUC, no una compra de 1998.
 */
function pickDate(lines: string[], now: Date): string | null {
  for (const line of lines) {
    const candidate = dateFromLine(line);

    if (candidate === null) {
      continue;
    }

    const parsed = new Date(`${candidate}T12:00:00.000Z`);
    if (Number.isNaN(parsed.getTime())) {
      continue;
    }

    const endOfToday = new Date(now);
    endOfToday.setHours(23, 59, 59, 999);

    if (parsed.getTime() > endOfToday.getTime()) {
      continue;
    }

    if (parsed.getUTCFullYear() < 2000) {
      continue;
    }

    return candidate;
  }

  return null;
}

function dateFromLine(line: string): string | null {
  const iso = ISO_DATE.exec(line);
  if (iso) {
    return buildDate(Number(iso[3]), Number(iso[2]), Number(iso[1]));
  }

  const text = TEXT_DATE.exec(line);
  if (text) {
    const month = MONTHS[text[2]!];
    if (month) {
      return buildDate(Number(text[1]), month, expandYear(Number(text[3])));
    }
  }

  const numeric = NUMERIC_DATE.exec(line);
  if (numeric) {
    // Ecuador escribe DD/MM/AAAA. Si el primer número supera 12 no hay duda;
    // si el segundo supera 12, entonces venía al revés.
    const first = Number(numeric[1]);
    const second = Number(numeric[2]);
    const year = expandYear(Number(numeric[3]));

    if (second > 12 && first <= 12) {
      return buildDate(second, first, year);
    }

    return buildDate(first, second, year);
  }

  return null;
}

function expandYear(year: number): number {
  return year < 100 ? 2000 + year : year;
}

function buildDate(day: number, month: number, year: number): string | null {
  if (month < 1 || month > 12 || day < 1 || day > 31) {
    return null;
  }

  const iso = `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
  const parsed = new Date(`${iso}T12:00:00.000Z`);

  // Rechaza 31 de febrero y compañía.
  if (Number.isNaN(parsed.getTime()) || parsed.getUTCDate() !== day || parsed.getUTCMonth() + 1 !== month) {
    return null;
  }

  return iso;
}

// --- comercio y método de pago ----------------------------------------------

/**
 * El nombre del comercio casi siempre está en las primeras líneas, antes del
 * RUC. Se toma la primera línea que parezca un nombre: con letras, sin
 * importes, y que no sea una cabecera conocida.
 */
function pickMerchant(lines: string[]): string | null {
  for (const line of lines.slice(0, 6)) {
    if (NOT_MERCHANT.test(line)) {
      continue;
    }

    if (/\d{2,}/.test(line)) {
      continue;
    }

    const letters = line.replace(/[^A-ZÁÉÍÓÚÑ ]/g, '').trim();

    if (letters.length < 3 || letters.length > 40) {
      continue;
    }

    return toTitleCase(letters);
  }

  return null;
}

function pickDocumentNumber(lines: string[]): string | null {
  for (const line of lines) {
    const match = DOCUMENT_NUMBER.exec(line);
    if (match) {
      return match[1]!;
    }
  }

  return null;
}

/**
 * §21: si el recibo dice EFECTIVO, se puede sugerir la cuenta Efectivo. Si
 * dice tarjeta, NO se adivina qué banco fue -- eso lo decide la persona o la
 * conciliación con un movimiento bancario real.
 */
function pickPaymentHint(lines: string[]): PaymentHintValue {
  const hasCash = lines.some((line) => CASH_LABEL.test(line));
  const hasCard = lines.some((line) => CARD_LABEL.test(line));

  if (hasCard && !hasCash) {
    return PaymentHint.Card;
  }

  if (hasCash && !hasCard) {
    return PaymentHint.Cash;
  }

  // Ambos o ninguno: no se adivina.
  return PaymentHint.Unknown;
}

function toTitleCase(value: string): string {
  return value
    .toLowerCase()
    .split(' ')
    .filter((word) => word.length > 0)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}
