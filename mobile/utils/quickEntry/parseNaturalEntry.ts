import { roundToCents } from './safeCalculator';

/**
 * §8: parser determinístico y local. "NO enviar todo inmediatamente a una IA
 * externa."
 *
 * Corre en el dispositivo, sin red y sin latencia, porque de eso depende que la
 * vista previa aparezca mientras la persona escribe. Reconoce frases financieras
 * simples en español ecuatoriano -- lo que la gente teclea de verdad ("5 almuerzo",
 * "8 uber ayer", "+50 me pagaron") y lo que dice en voz alta ("gasté seis dólares
 * en almuerzo") -- y para todo lo demás devuelve lo que sí entendió, con su
 * confianza, en vez de inventar el resto.
 *
 * Lo que este archivo NO hace, a propósito:
 *
 *  - No decide la categoría final. Propone una pista de categoría por palabra clave
 *    (§10, prioridad 3); resolverla contra las categorías REALES de la persona es
 *    trabajo de `resolveCategory.ts`, y si no hay coincidencia decide el motor de
 *    reglas del servidor, que es el que ya sabe lo que esta persona le ha enseñado.
 *  - No inventa categorías (§43).
 *  - No guarda nada. Un parseo es una propuesta hasta que alguien toca Guardar.
 */

export type ParsedDirection = 'Expense' | 'Income';

/** §12: confianza por campo, porque no todos los campos fallan igual de grave. */
export interface FieldConfidence {
  amount: number;
  direction: number;
  description: number;
  date: number;
  category: number;
}

export interface ParsedEntry {
  /** Magnitud, siempre positiva. Null cuando no se reconoció ningún monto. */
  amount: number | null;
  direction: ParsedDirection;
  /** El concepto, ya presentable ("Uber", "Almuerzo"). Null si no quedó nada. */
  description: string | null;
  /** Fecha ISO, o null para "ahora". */
  occurredAt: string | null;
  /** Etiqueta legible de la fecha reconocida ("Ayer", "Lunes"), para la vista previa. */
  dateLabel: string | null;
  /** §10: pista de categoría por palabra clave. Un CÓDIGO del catálogo, nunca un nombre inventado. */
  categoryCode: string | null;
  confidence: FieldConfidence;
  /**
   * §12: "si monto es ambiguo, NO guardar." Es la única condición que bloquea:
   * sin categoría se guarda igual, y sin fecha se usa ahora.
   */
  canSave: boolean;
}

/**
 * §10: las palabras clave apuntan a CÓDIGOS del catálogo de categorías que ya
 * existe en el backend (CategoryCodes), no a nombres nuevos. Si Fino renombra
 * "Comida" a "Alimentación", esto sigue funcionando; si alguien inventara aquí una
 * categoría "Restaurantes", nunca resolvería contra nada.
 *
 * La lista es corta a propósito. Cubre lo que se paga en efectivo en Ecuador y para
 * de crecer ahí: extender esto indefinidamente sería reimplementar en el teléfono
 * el motor de reglas que el servidor ya tiene, y que además aprende.
 */
const CATEGORY_KEYWORDS: ReadonlyArray<readonly [string, readonly string[]]> = [
  ['COMIDA', ['almuerzo', 'almuerzos', 'desayuno', 'cena', 'comida', 'restaurante', 'cafe', 'café',
    'cafeteria', 'cafetería', 'merienda', 'snack', 'pan', 'panaderia', 'panadería', 'helado',
    'pizza', 'hamburguesa', 'seco', 'encebollado', 'bolon', 'bolón', 'colada']],
  ['TRANSPORTE', ['uber', 'taxi', 'bus', 'buseta', 'pasaje', 'pasajes', 'gasolina', 'combustible',
    'diesel', 'peaje', 'parqueo', 'parqueadero', 'metrovia', 'metrovía', 'cabify', 'indrive',
    'didi', 'moto', 'carrera']],
  ['SALUD', ['farmacia', 'medicina', 'medicinas', 'pastillas', 'doctor', 'clinica', 'clínica',
    'consulta', 'dentista', 'laboratorio']],
  ['SUPERMERCADO', ['supermercado', 'mercado', 'supermaxi', 'mi comisariato', 'tia', 'tía',
    'akí', 'aki', 'vivere', 'verduras', 'frutas', 'carne', 'abarrotes']],
  ['COMPRAS', ['compras', 'ropa', 'zapatos', 'regalo', 'regalos', 'ferreteria', 'ferretería',
    'papeleria', 'papelería', 'libreria', 'librería']],
  ['SERVICIOS', ['luz', 'agua', 'internet', 'telefono', 'teléfono', 'recarga', 'saldo', 'gas',
    'arriendo', 'alquiler', 'planilla']],
  ['ENTRETENIMIENTO', ['cine', 'juego', 'juegos', 'salida', 'fiesta', 'concierto', 'bar']],
  ['EDUCACION', ['curso', 'matricula', 'matrícula', 'pension', 'pensión', 'libro', 'libros',
    'universidad', 'colegio', 'utiles', 'útiles']],
  ['INGRESOS', ['sueldo', 'salario', 'pago', 'pagaron', 'freelance', 'venta', 'vendi', 'vendí',
    'propina', 'bono', 'prestamo', 'préstamo']],
];

/**
 * §7: "+50 me pagaron". Los verbos que delatan que entra dinero en vez de salir.
 * Se comprueban como palabras completas, no como subcadenas: "cobre" (el metal) no
 * debe convertir un gasto en ingreso.
 */
const INCOME_WORDS = [
  'ingreso', 'ingresos', 'cobre', 'cobré', 'cobro', 'recibi', 'recibí', 'recibo',
  'gane', 'gané', 'gano', 'pagaron', 'dieron', 'deposito', 'depósito', 'depositaron',
  'vendi', 'vendí', 'venta', 'sueldo', 'salario', 'propina', 'bono', 'devolvieron',
  'devolucion', 'devolución', 'reembolso',
];

const EXPENSE_WORDS = [
  'gaste', 'gasté', 'gasto', 'pague', 'pagué', 'pago', 'compre', 'compré', 'compra',
  'invertí', 'inverti', 'saque', 'saqué',
];

/**
 * §15: la voz dice "seis dólares", no "6". Solo los números que aparecen de verdad
 * en montos hablados del día a día; por encima de esto la gente vuelve a decir
 * cifras ("ciento veinte" se dice, pero "mil trescientos cuarenta y siete" ya no).
 */
const SPOKEN_NUMBERS: Readonly<Record<string, number>> = {
  cero: 0, un: 1, uno: 1, una: 1, dos: 2, tres: 3, cuatro: 4, cinco: 5, seis: 6,
  siete: 7, ocho: 8, nueve: 9, diez: 10, once: 11, doce: 12, trece: 13, catorce: 14,
  quince: 15, dieciseis: 16, dieciséis: 16, diecisiete: 17, dieciocho: 18,
  diecinueve: 19, veinte: 20, veintiuno: 21, veintidos: 22, veintidós: 22,
  veinticinco: 25, treinta: 30, cuarenta: 40, cincuenta: 50, sesenta: 60,
  setenta: 70, ochenta: 80, noventa: 90, cien: 100, ciento: 100, doscientos: 200,
  trescientos: 300, cuatrocientos: 400, quinientos: 500, mil: 1000,
};

/** Palabras que no aportan nada al concepto y solo lo ensucian. */
const FILLER_WORDS = new Set([
  'de', 'del', 'en', 'el', 'la', 'los', 'las', 'un', 'una', 'unos', 'unas',
  'por', 'para', 'con', 'y', 'a', 'al', 'me', 'mi', 'se', 'lo', 'que',
  'dolar', 'dolares', 'dólar', 'dólares', 'usd', 'plata', 'efectivo',
  ...INCOME_WORDS,
  ...EXPENSE_WORDS,
]);

const WEEKDAYS: ReadonlyArray<readonly [string, number]> = [
  ['domingo', 0], ['lunes', 1], ['martes', 2], ['miercoles', 3], ['miércoles', 3],
  ['jueves', 4], ['viernes', 5], ['sabado', 6], ['sábado', 6],
];

const WEEKDAY_LABELS = ['Domingo', 'Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado'];

/** Quita acentos para comparar, sin tocar el texto que se mostrará. */
function fold(value: string): string {
  return value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
}

function hasWord(foldedText: string, word: string): boolean {
  // Límites de palabra sobre texto ya sin acentos. `\b` no sirve con la ñ, así que
  // se comprueba explícitamente que alrededor no haya letras ni dígitos.
  const index = foldedText.indexOf(fold(word));
  if (index < 0) {
    return false;
  }

  const before = foldedText[index - 1];
  const after = foldedText[index + fold(word).length];
  const isLetter = (c: string | undefined) => c !== undefined && /[a-z0-9ñ]/.test(c);

  return !isLetter(before) && !isLetter(after);
}

/**
 * §9: "5", "5.5", "5,50", "$5", "$ 5", "USD 5", "5 dólares", "5 dolares".
 *
 * El orden importa. Primero se buscan las formas que llevan una marca de moneda
 * explícita, porque son inequívocas; solo si no hay ninguna se acepta un número
 * suelto. Así "10 farmacia lunes" no corre el riesgo de leer un día como monto, y
 * un "2026" perdido en el texto no se confunde con dos mil veintiséis dólares.
 */
interface AmountMatch {
  value: number;
  /** Rango en el texto ORIGINAL, para poder quitarlo del concepto. */
  start: number;
  end: number;
  confidence: number;
  explicitSign: ParsedDirection | null;
}

function findAmount(text: string): AmountMatch | null {
  const patterns: ReadonlyArray<{ regex: RegExp; confidence: number; group: number }> = [
    // "$5", "$ 5,50", "+$20", "-$20"
    { regex: /([+-]?)\s*\$\s*(\d+(?:[.,]\d{1,2})?)/, confidence: 1, group: 2 },
    // "USD 5", "5 USD"
    { regex: /([+-]?)\s*usd\s*(\d+(?:[.,]\d{1,2})?)/i, confidence: 1, group: 2 },
    { regex: /([+-]?)\s*(\d+(?:[.,]\d{1,2})?)\s*usd\b/i, confidence: 1, group: 2 },
    // "5 dólares", "6 dolares"
    { regex: /([+-]?)\s*(\d+(?:[.,]\d{1,2})?)\s*d[oó]lar(?:es)?\b/i, confidence: 1, group: 2 },
    // "+50", "-8": el signo explícito es una señal de intención muy fuerte (§7).
    { regex: /(^|\s)([+-])\s*(\d+(?:[.,]\d{1,2})?)/, confidence: 1, group: 3 },
    // Un número suelto: lo más frecuente ("5 almuerzo") y también lo más ambiguo.
    { regex: /(^|\s)()(\d+(?:[.,]\d{1,2})?)(?=\s|$)/, confidence: 0.9, group: 3 },
  ];

  for (const { regex, confidence, group } of patterns) {
    const match = regex.exec(text);
    if (!match) {
      continue;
    }

    const raw = match[group];
    const value = Number(raw.replace(',', '.'));
    if (!Number.isFinite(value) || value <= 0) {
      continue;
    }

    // Un año suelto casi nunca es un monto. Se descarta solo cuando NO hay marca de
    // moneda: "$2026" sí es un monto, "compré algo en 2026" no.
    if (confidence < 1 && /^(19|20)\d{2}$/.test(raw)) {
      continue;
    }

    const signGroup = match[2] === '+' || match[2] === '-' ? match[2] : match[1];
    const explicitSign =
      signGroup === '+' ? 'Income' : signGroup === '-' ? 'Expense' : null;

    const start = match.index + (match[0].length - match[0].trimStart().length);

    return {
      value: roundToCents(value),
      start,
      end: match.index + match[0].length,
      confidence,
      explicitSign: explicitSign as ParsedDirection | null,
    };
  }

  // §15: "gasté seis dólares", "pagué veinte de gasolina".
  const folded = fold(text);
  for (const [word, value] of Object.entries(SPOKEN_NUMBERS)) {
    if (!hasWord(folded, word)) {
      continue;
    }

    const index = folded.indexOf(fold(word));
    return {
      value,
      start: index,
      end: index + word.length,
      // Un número escrito con letras es menos fiable que uno en dígitos: "un
      // almuerzo" no quiere decir "$1 de almuerzo". Por debajo del umbral de
      // guardado automático a propósito.
      confidence: 0.75,
      explicitSign: null,
    };
  }

  return null;
}

interface DateMatch {
  date: Date;
  label: string;
  start: number;
  end: number;
  confidence: number;
}

/**
 * §8: "hoy", "ayer", "anteayer" y los nombres de los días. "Interpretar el día más
 * reciente razonable": un martes, "lunes" es ayer; un lunes, "lunes" es hoy (no
 * hace ocho días).
 */
function findDate(text: string, now: Date): DateMatch | null {
  const folded = fold(text);

  const startOfDay = (base: Date, deltaDays: number): Date => {
    const date = new Date(base);
    date.setDate(date.getDate() + deltaDays);
    // Se conserva la HORA actual, no medianoche: un gasto de ayer a las 15:00 es más
    // parecido a la verdad que uno a las 00:00, y evita que caiga en el día anterior
    // al convertir husos horarios.
    return date;
  };

  const relative: ReadonlyArray<readonly [string, number, string]> = [
    ['anteayer', -2, 'Anteayer'],
    ['antier', -2, 'Anteayer'],
    ['ayer', -1, 'Ayer'],
    ['hoy', 0, 'Hoy'],
  ];

  for (const [word, delta, label] of relative) {
    if (!hasWord(folded, word)) {
      continue;
    }

    const index = folded.indexOf(word);
    return {
      date: startOfDay(now, delta),
      label,
      start: index,
      end: index + word.length,
      confidence: 1,
    };
  }

  for (const [word, weekday] of WEEKDAYS) {
    if (!hasWord(folded, word)) {
      continue;
    }

    // Cuántos días hay que retroceder hasta el día pedido. 0 significa hoy mismo,
    // que es la lectura correcta de "lunes" dicho un lunes.
    const back = (now.getDay() - weekday + 7) % 7;
    const index = folded.indexOf(fold(word));

    return {
      date: startOfDay(now, -back),
      label: back === 0 ? 'Hoy' : back === 1 ? 'Ayer' : WEEKDAY_LABELS[weekday],
      start: index,
      end: index + word.length,
      // Menos que "ayer": nombrar un día es inherentemente más ambiguo (¿este lunes
      // o el anterior?) que decir "ayer".
      confidence: 0.85,
    };
  }

  return null;
}

function findDirection(text: string, explicitSign: ParsedDirection | null): {
  direction: ParsedDirection;
  confidence: number;
} {
  // Un "+" o un "-" escritos a mano son una declaración de intención, y ganan a
  // cualquier verbo del texto.
  if (explicitSign) {
    return { direction: explicitSign, confidence: 1 };
  }

  const folded = fold(text);

  if (EXPENSE_WORDS.some((word) => hasWord(folded, word))) {
    return { direction: 'Expense', confidence: 1 };
  }

  if (INCOME_WORDS.some((word) => hasWord(folded, word))) {
    return { direction: 'Income', confidence: 0.95 };
  }

  // §3: gasto por defecto. Confianza media, no baja: acertar el 90% de las veces no
  // es adivinar, pero tampoco es saberlo.
  return { direction: 'Expense', confidence: 0.6 };
}

/**
 * Pista de categoría a partir de un texto libre.
 *
 * Exportada porque el escaneo de facturas necesita exactamente esto para el
 * nombre del comercio ("SUPERMAXI" -> SUPERMERCADO). Duplicar el diccionario
 * allí habría hecho que las dos vías se separaran en cuanto alguien añadiera
 * una palabra a una sola de ellas.
 *
 * Sigue siendo la prioridad 3 de §10: devuelve un CÓDIGO, nunca una categoría
 * inventada, y `null` es una respuesta válida -- ahí decide el motor de
 * reglas del servidor, que es el que conoce lo que esta persona ha enseñado.
 */
export function findCategoryCode(description: string | null, direction: ParsedDirection): {
  code: string | null;
  confidence: number;
} {
  if (!description) {
    return { code: null, confidence: 0 };
  }

  const folded = fold(description);

  for (const [code, keywords] of CATEGORY_KEYWORDS) {
    // Las palabras clave de ingresos solo cuentan si el movimiento ES un ingreso:
    // "pago de gasolina" es transporte, no un ingreso.
    if (code === 'INGRESOS' && direction !== 'Income') {
      continue;
    }

    for (const keyword of keywords) {
      if (hasWord(folded, keyword)) {
        return { code, confidence: 0.82 };
      }
    }
  }

  return { code: null, confidence: 0 };
}

/** Deja el concepto presentable: sin relleno, sin monto, sin fecha, capitalizado. */
function buildDescription(text: string, cuts: ReadonlyArray<{ start: number; end: number }>): string | null {
  let remaining = text;

  // Se recortan de atrás hacia delante para que los índices anteriores sigan
  // siendo válidos mientras se corta.
  for (const cut of [...cuts].sort((a, b) => b.start - a.start)) {
    remaining = remaining.slice(0, cut.start) + ' ' + remaining.slice(cut.end);
  }

  const words = remaining
    .replace(/[$+]/g, ' ')
    .split(/\s+/)
    .map((word) => word.trim())
    .filter((word) => word.length > 0)
    .filter((word) => !FILLER_WORDS.has(fold(word)))
    // Un número que sobrevivió al recorte del monto es ruido, no concepto.
    .filter((word) => !/^\d+([.,]\d+)?$/.test(word));

  if (words.length === 0) {
    return null;
  }

  const joined = words.join(' ');
  return joined.charAt(0).toLocaleUpperCase('es-EC') + joined.slice(1);
}

/**
 * Punto de entrada. `now` se inyecta para que las pruebas no dependan del día en
 * que se ejecutan.
 */
export function parseNaturalEntry(input: string, now: Date = new Date()): ParsedEntry {
  const text = input.trim();

  const empty: ParsedEntry = {
    amount: null,
    direction: 'Expense',
    description: null,
    occurredAt: null,
    dateLabel: null,
    categoryCode: null,
    confidence: { amount: 0, direction: 0, description: 0, date: 0, category: 0 },
    canSave: false,
  };

  if (text.length === 0) {
    return empty;
  }

  const amountMatch = findAmount(text);
  const dateMatch = findDate(text, now);
  const { direction, confidence: directionConfidence } = findDirection(
    text,
    amountMatch?.explicitSign ?? null,
  );

  const cuts = [
    ...(amountMatch ? [{ start: amountMatch.start, end: amountMatch.end }] : []),
    ...(dateMatch ? [{ start: dateMatch.start, end: dateMatch.end }] : []),
  ];

  const description = buildDescription(text, cuts);
  const { code: categoryCode, confidence: categoryConfidence } = findCategoryCode(description, direction);

  return {
    amount: amountMatch?.value ?? null,
    direction,
    description,
    occurredAt: dateMatch ? dateMatch.date.toISOString() : null,
    dateLabel: dateMatch?.label ?? null,
    categoryCode,
    confidence: {
      amount: amountMatch?.confidence ?? 0,
      direction: directionConfidence,
      description: description ? 0.9 : 0,
      // §12: sin fecha reconocida se usa "ahora", y eso no es una suposición
      // arriesgada -- es lo que la persona quiso decir el 95% de las veces.
      date: dateMatch?.confidence ?? 0.95,
      category: categoryConfidence,
    },
    // §12: "si monto es ambiguo, NO guardar." Lo único que bloquea es el monto:
    // sin categoría se guarda igual (§43) y sin fecha se usa ahora.
    canSave: amountMatch !== null && amountMatch.confidence >= 0.75,
  };
}

/**
 * §11: "posteriormente se puede activar autoguardado con alta confianza."
 *
 * La recomendación explícita del spec para la primera versión es NO autoguardar
 * texto natural, así que esto existe pero devuelve false salvo que se active a
 * conciencia. Está aquí, y no repartido por la interfaz, para que encender el
 * autoguardado el día que se decida sea cambiar una constante y no auditar
 * pantallas.
 */
export const AUTO_SAVE_ENABLED = false;

export function shouldAutoSave(entry: ParsedEntry): boolean {
  if (!AUTO_SAVE_ENABLED) {
    return false;
  }

  return (
    entry.canSave &&
    entry.confidence.amount >= 1 &&
    entry.confidence.direction >= 0.95 &&
    entry.confidence.date >= 0.95
  );
}
