import { parseReceiptText } from './parseReceiptText';
import { ReceiptWarning, emptyExtraction, type ReceiptExtractionResult, type ReceiptExtractor } from './types';

/**
 * §14-15: la estrategia de extracción, detrás de una interfaz.
 *
 * Hoy hay una sola implementación y corre ENTERA en el teléfono:
 *
 *   imagen -> ML Kit (OCR on-device) -> texto -> parseReceiptText -> estructura
 *
 * Ni la imagen ni el texto salen del dispositivo en ningún momento. Una
 * factura ecuatoriana lleva RUC, a veces el nombre del cliente, la dirección
 * del local y los últimos dígitos de una tarjeta: mandarla a un servicio
 * externo para leer un número sería un intercambio pésimo.
 *
 * §15 deja la puerta abierta a una fase 3 híbrida (usar visión solo cuando el
 * parser local no encuentre el total con confianza). Esa implementación sería
 * OTRA clase que cumpla `ReceiptExtractor`; el flujo, la pantalla de revisión
 * y la creación del movimiento no se enterarían. Pero no se añade sin
 * documentarla en la política de privacidad y pedir permiso por factura.
 */

interface MlKitTextResult {
  text: string;
}

interface MlKitModule {
  default?: { recognize(uri: string): Promise<MlKitTextResult> };
  recognize?(uri: string): Promise<MlKitTextResult>;
}

export class MlKitReceiptExtractor implements ReceiptExtractor {
  readonly name = 'mlkit';

  private constructor(private readonly recognize: (uri: string) => Promise<MlKitTextResult>) {}

  /** Null si el módulo nativo no está instalado en este build. */
  static create(): MlKitReceiptExtractor | null {
    try {
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const module = require('@react-native-ml-kit/text-recognition') as MlKitModule;
      const recognize = module?.default?.recognize ?? module?.recognize;

      if (typeof recognize !== 'function') {
        return null;
      }

      const target = module.default ?? module;
      return new MlKitReceiptExtractor((uri) => recognize.call(target, uri));
    } catch {
      return null;
    }
  }

  async extract(imageUri: string): Promise<ReceiptExtractionResult> {
    try {
      const result = await this.recognize(imageUri);
      return parseReceiptText(result?.text ?? '');
    } catch {
      // §30: que el OCR falle no es un error de la app. Se devuelve un
      // resultado vacío y la pantalla ofrece repetir la foto o registrar a
      // mano.
      return emptyExtraction([ReceiptWarning.LowText, ReceiptWarning.NoTotal]);
    }
  }
}

/**
 * Extractor sobre texto ya conocido. Es el que usan los tests y el que
 * permite probar el flujo entero sin cámara ni módulo nativo.
 */
export class TextReceiptExtractor implements ReceiptExtractor {
  readonly name = 'text';

  constructor(private readonly text: string) {}

  async extract(): Promise<ReceiptExtractionResult> {
    return parseReceiptText(this.text);
  }
}

/** Devuelve null cuando no hay OCR disponible en este build. */
export function createReceiptExtractor(): ReceiptExtractor | null {
  return MlKitReceiptExtractor.create();
}
