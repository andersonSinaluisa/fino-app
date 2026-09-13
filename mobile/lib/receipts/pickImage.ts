import { ReceiptSource, type ReceiptSourceValue } from './types';

/**
 * §4 y §8: conseguir la imagen, sin que el resto del flujo sepa de qué
 * librería viene.
 *
 * Se usa `expo-image-picker` para las dos vías (cámara y galería) en vez de
 * montar una pantalla de cámara propia. Es una decisión deliberada: la cámara
 * del sistema ya trae enfoque, flash, HDR y --en iOS-- el recorte automático
 * de documentos, que es mejor que cualquier marco que dibujemos nosotros, y
 * ya está traducida y es accesible. §5 describe una cámara a medida; se puede
 * añadir después como otra implementación de esta misma función, pero para
 * FASE 1 la del sistema da mejor resultado con muchísimo menos código que
 * mantener.
 *
 * §4: el permiso se pide AQUÍ, la primera vez que la persona toca "Tomar
 * foto". Nunca al instalar ni al abrir la app.
 */

export const PickOutcome = {
  Picked: 'picked',
  Cancelled: 'cancelled',
  PermissionDenied: 'permission_denied',
  Unavailable: 'unavailable',
} as const;

export type PickOutcomeValue = (typeof PickOutcome)[keyof typeof PickOutcome];

export interface PickedImage {
  outcome: PickOutcomeValue;
  uri: string | null;
  width: number | null;
  height: number | null;
  source: ReceiptSourceValue;
  /** True cuando el permiso se puede volver a pedir; false manda a Configuración. */
  canAskAgain: boolean;
}

interface PermissionResponse {
  granted: boolean;
  canAskAgain: boolean;
}

interface PickerAsset {
  uri: string;
  width?: number;
  height?: number;
}

interface PickerResult {
  canceled: boolean;
  assets?: PickerAsset[] | null;
}

interface ImagePickerModule {
  requestCameraPermissionsAsync?: () => Promise<PermissionResponse>;
  requestMediaLibraryPermissionsAsync?: () => Promise<PermissionResponse>;
  launchCameraAsync?: (options: unknown) => Promise<PickerResult>;
  launchImageLibraryAsync?: (options: unknown) => Promise<PickerResult>;
  MediaTypeOptions?: { Images: unknown };
}

function loadPicker(): ImagePickerModule | null {
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    return require('expo-image-picker') as ImagePickerModule;
  } catch {
    return null;
  }
}

/**
 * §7: `allowsEditing` da recorte y rotación básicos usando el editor del
 * sistema. Exactamente lo que pide el spec, y ni una línea de editor de
 * imágenes propio.
 *
 * La calidad se deja alta: el recorte a 1600px y la compresión los hace
 * después `prepareReceiptImage`, que además quita el EXIF.
 */
const PICKER_OPTIONS = {
  allowsEditing: true,
  quality: 1,
  exif: false,
} as const;

function unavailable(source: ReceiptSourceValue): PickedImage {
  return { outcome: PickOutcome.Unavailable, uri: null, width: null, height: null, source, canAskAgain: true };
}

function fromResult(result: PickerResult, source: ReceiptSourceValue): PickedImage {
  const asset = result.assets?.[0];

  if (result.canceled || !asset?.uri) {
    return { outcome: PickOutcome.Cancelled, uri: null, width: null, height: null, source, canAskAgain: true };
  }

  return {
    outcome: PickOutcome.Picked,
    uri: asset.uri,
    width: asset.width ?? null,
    height: asset.height ?? null,
    source,
    canAskAgain: true,
  };
}

export async function takeReceiptPhoto(): Promise<PickedImage> {
  const picker = loadPicker();

  if (!picker?.launchCameraAsync || !picker.requestCameraPermissionsAsync) {
    return unavailable(ReceiptSource.Camera);
  }

  const permission = await picker.requestCameraPermissionsAsync();

  if (!permission.granted) {
    return {
      outcome: PickOutcome.PermissionDenied,
      uri: null,
      width: null,
      height: null,
      source: ReceiptSource.Camera,
      canAskAgain: permission.canAskAgain,
    };
  }

  const result = await picker.launchCameraAsync({
    ...PICKER_OPTIONS,
    mediaTypes: picker.MediaTypeOptions?.Images,
  });

  return fromResult(result, ReceiptSource.Camera);
}

export async function chooseReceiptPhoto(): Promise<PickedImage> {
  const picker = loadPicker();

  if (!picker?.launchImageLibraryAsync) {
    return unavailable(ReceiptSource.Gallery);
  }

  // En iOS el selector moderno no necesita permiso de fototeca; en Android sí
  // en versiones antiguas. Se pide solo si el módulo lo expone, y un rechazo
  // no bloquea: se intenta igual y el propio selector decide.
  if (picker.requestMediaLibraryPermissionsAsync) {
    const permission = await picker.requestMediaLibraryPermissionsAsync();

    if (!permission.granted) {
      return {
        outcome: PickOutcome.PermissionDenied,
        uri: null,
        width: null,
        height: null,
        source: ReceiptSource.Gallery,
        canAskAgain: permission.canAskAgain,
      };
    }
  }

  const result = await picker.launchImageLibraryAsync({
    ...PICKER_OPTIONS,
    mediaTypes: picker.MediaTypeOptions?.Images,
  });

  return fromResult(result, ReceiptSource.Gallery);
}
