/**
 * §41-42: dejar la foto lista antes de analizarla.
 *
 * Tres cosas, en este orden:
 *
 *  1. Corregir la orientación. Una foto tomada de lado llega con la
 *     orientación en los metadatos EXIF y no en los píxeles; el OCR lee los
 *     píxeles, así que sin esto el texto le llega girado 90 grados.
 *  2. Reducir la resolución si es excesiva. 1600 px de ancho es de sobra para
 *     leer la letra pequeña de un recibo, y una foto de 12 MP solo hace el
 *     OCR más lento.
 *  3. Recomprimir. Al re-codificar la imagen se pierden TODOS los metadatos
 *     EXIF, incluida la GEOLOCALIZACIÓN (§42). FINO no necesita saber dónde
 *     estabas para leer un total, y esa coordenada es dato personal que no
 *     tiene por qué acompañar a la imagen ni un segundo más.
 *
 * La calidad (0.85) está alta a propósito: comprimir de más emborrona
 * precisamente la letra pequeña que hay que leer.
 */

const MAX_WIDTH = 1600;
const QUALITY = 0.85;

interface ManipulatorModule {
  manipulateAsync?: (
    uri: string,
    actions: { resize?: { width?: number } }[],
    options: { compress: number; format: unknown },
  ) => Promise<{ uri: string; width: number; height: number }>;
  SaveFormat?: { JPEG: unknown };
}

export interface PreparedReceiptImage {
  uri: string;
  width: number | null;
  height: number | null;
  /** False cuando no se pudo procesar y se usa la imagen original tal cual. */
  processed: boolean;
}

export async function prepareReceiptImage(
  uri: string,
  sourceWidth?: number,
): Promise<PreparedReceiptImage> {
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const manipulator = require('expo-image-manipulator') as ManipulatorModule;
    const manipulate = manipulator?.manipulateAsync;
    const jpeg = manipulator?.SaveFormat?.JPEG;

    if (typeof manipulate !== 'function' || jpeg === undefined) {
      return { uri, width: null, height: null, processed: false };
    }

    const actions =
      sourceWidth !== undefined && sourceWidth > MAX_WIDTH ? [{ resize: { width: MAX_WIDTH } }] : [];

    const result = await manipulate(uri, actions, { compress: QUALITY, format: jpeg });

    return { uri: result.uri, width: result.width, height: result.height, processed: true };
  } catch {
    // Que no se pueda preparar la imagen no puede tumbar el escaneo: se
    // intenta el OCR con la original.
    return { uri, width: null, height: null, processed: false };
  }
}

/**
 * §6: control de calidad mínimo, sin bloquear.
 *
 * No se analiza el contenido de la imagen (eso exigiría procesar píxeles y no
 * lo vale): solo se mira si la resolución es tan baja que el OCR no tiene
 * nada que hacer. Si lo es, la UI sugiere repetir la foto pero deja
 * continuar, porque un aviso que no se puede saltar es peor que una foto
 * mediocre.
 */
export function looksTooSmall(width: number | null, height: number | null): boolean {
  if (width === null || height === null) {
    return false;
  }

  return Math.max(width, height) < 600;
}
