import { parseReceiptText, parseAmount } from '../../lib/receipts/parseReceiptText';
import { PaymentHint, ReceiptWarning } from '../../lib/receipts/types';

const NOW = new Date('2026-09-11T12:00:00.000Z');

function parse(text: string) {
  return parseReceiptText(text, { now: NOW });
}

describe('parseReceiptText — determinar el total (§13)', () => {
  it('CASO 1: subtotal + IVA + total', () => {
    const result = parse(`
      SUBTOTAL 20.00
      IVA 3.00
      TOTAL 23.00
    `);

    expect(result.total).toBe(23);
    expect(result.subtotal).toBe(20);
    expect(result.tax).toBe(3);
  });

  it('CASO 2: no confunde EFECTIVO recibido ni CAMBIO con el total', () => {
    const result = parse(`
      TOTAL 23.00
      EFECTIVO 25.00
      CAMBIO 2.00
    `);

    expect(result.total).toBe(23);
  });

  it('CASO 3: VALOR TOTAL', () => {
    expect(parse('VALOR TOTAL 100.50').total).toBe(100.5);
  });

  it('CASO 4: TOTAL A PAGAR gana a un SUBTOTAL', () => {
    const result = parse(`
      SUBTOTAL 10
      TOTAL A PAGAR 11.50
    `);

    expect(result.total).toBe(11.5);
  });

  it('CASO 5: sin total claro no inventa nada', () => {
    const result = parse(`
      GRACIAS POR SU COMPRA
      VUELVA PRONTO
    `);

    expect(result.total).toBeNull();
    expect(result.warnings).toContain(ReceiptWarning.NoTotal);
  });

  it('SUBTOTAL nunca se lee como TOTAL aunque contenga la palabra', () => {
    const result = parse('SUBTOTAL SIN IMPUESTOS 20.00');

    expect(result.total).toBeNull();
    expect(result.subtotal).toBe(20);
  });

  it('avisa de que la foto puede estar cortada si hay subtotal pero no total', () => {
    const result = parse(`
      SUBTOTAL 20.00
      IVA 15% 3.00
    `);

    expect(result.total).toBeNull();
    expect(result.warnings).toContain(ReceiptWarning.PossiblyCropped);
  });

  it('recibo ecuatoriano completo con IVA 15%', () => {
    const result = parse(`
      SUPERMAXI
      RUC 1790016919001
      FACTURA 001-002-000123456
      FECHA 11/09/2026
      SUBTOTAL SIN IMPUESTOS 21.95
      IVA 15% 2.63
      TOTAL A PAGAR 24.58
      EFECTIVO 30.00
      CAMBIO 5.42
    `);

    expect(result.total).toBe(24.58);
    expect(result.subtotal).toBe(21.95);
    expect(result.tax).toBe(2.63);
    expect(result.date).toBe('2026-09-11');
    expect(result.merchantName).toBe('Supermaxi');
    expect(result.documentNumber).toBe('001-002-000123456');
    expect(result.paymentHint).toBe(PaymentHint.Cash);
    expect(result.warnings).not.toContain(ReceiptWarning.TotalsInconsistent);
  });

  it('avisa cuando subtotal + IVA no cuadran, pero no corrige el total', () => {
    const result = parse(`
      SUBTOTAL 20.00
      IVA 3.00
      TOTAL 99.00
    `);

    expect(result.total).toBe(99);
    expect(result.warnings).toContain(ReceiptWarning.TotalsInconsistent);
    expect(result.fieldConfidence.total).toBeLessThan(0.6);
  });

  it('la coherencia aritmética sube la confianza', () => {
    const consistent = parse('SUBTOTAL 20.00\nIVA 3.00\nTOTAL A PAGAR 23.00');
    expect(consistent.fieldConfidence.total).toBeGreaterThan(0.9);
  });

  it('no acepta un total de cero ni negativo', () => {
    expect(parse('TOTAL 0.00').total).toBeNull();
  });
});

describe('parseReceiptText — fecha (§17)', () => {
  it('lee DD/MM/AAAA', () => {
    expect(parse('FECHA 09/03/2026').date).toBe('2026-03-09');
  });

  it('lee formato con mes en texto', () => {
    expect(parse('11-SEP-2026').date).toBe('2026-09-11');
  });

  it('lee ISO', () => {
    expect(parse('2026-09-11').date).toBe('2026-09-11');
  });

  it('corrige el orden cuando el segundo número no puede ser un mes', () => {
    expect(parse('FECHA 03/25/2026').date).toBe('2026-03-25');
  });

  it('descarta una fecha futura', () => {
    const result = parse('FECHA 11/09/2027\nTOTAL 10.00');

    expect(result.date).toBeNull();
    expect(result.warnings).toContain(ReceiptWarning.UnreasonableDate);
  });

  it('descarta fechas imposibles', () => {
    expect(parse('FECHA 31/02/2026').date).toBeNull();
  });

  it('ignora años anteriores a 2000 (casi siempre el OCR leyendo mal)', () => {
    expect(parse('AUTORIZACION 12/05/1998').date).toBeNull();
  });
});

describe('parseReceiptText — método de pago (§21)', () => {
  it('detecta efectivo', () => {
    expect(parse('TOTAL 10.00\nEFECTIVO 10.00').paymentHint).toBe(PaymentHint.Cash);
  });

  it('detecta tarjeta', () => {
    expect(parse('TOTAL 10.00\nVISA ****1234').paymentHint).toBe(PaymentHint.Card);
  });

  it('no adivina si aparecen los dos', () => {
    expect(parse('EFECTIVO 5.00\nTARJETA 5.00').paymentHint).toBe(PaymentHint.Unknown);
  });

  it('no adivina si no aparece ninguno', () => {
    expect(parse('TOTAL 10.00').paymentHint).toBe(PaymentHint.Unknown);
  });
});

describe('parseAmount', () => {
  it('acepta punto y coma como decimal', () => {
    expect(parseAmount('24.58')).toBe(24.58);
    expect(parseAmount('24,58')).toBe(24.58);
  });

  it('acepta separador de miles en los dos formatos', () => {
    expect(parseAmount('1.234,56')).toBe(1234.56);
    expect(parseAmount('1,234.56')).toBe(1234.56);
  });

  it('acepta enteros', () => {
    expect(parseAmount('10')).toBe(10);
  });

  it('trata un separador de tres dígitos como miles', () => {
    expect(parseAmount('1.234')).toBe(1234);
  });

  it('devuelve null con basura', () => {
    expect(parseAmount('')).toBeNull();
    expect(parseAmount('ABC')).toBeNull();
  });
});

describe('parseReceiptText — entrada degenerada', () => {
  it('texto vacío no revienta', () => {
    const result = parse('');
    expect(result.total).toBeNull();
    expect(result.warnings).toContain(ReceiptWarning.LowText);
  });

  it('nunca devuelve NaN ni Infinity', () => {
    const result = parse('TOTAL 999999999999999999999.99');
    expect(result.total === null || Number.isFinite(result.total)).toBe(true);
  });
});
