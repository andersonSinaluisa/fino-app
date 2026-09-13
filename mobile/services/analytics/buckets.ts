import {
  CountBucket,
  DurationBucket,
  FileFormat,
  SourceCountBucket,
  type CountBucketValue,
  type DurationBucketValue,
  type FileFormatValue,
} from './events';

/**
 * §18: una duración en tramos, no en milisegundos.
 *
 * `durationMs: 1874` responde lo mismo que `under_2s` para la pregunta que
 * importa ("¿el registro rápido baja de los 3 segundos?"), pero un
 * milisegundo exacto es casi un identificador: combinado con la hora del
 * evento distingue a una persona de otra. El tramo no.
 */
export function toDurationBucket(elapsedMs: number): DurationBucketValue {
  if (!Number.isFinite(elapsedMs) || elapsedMs < 0) {
    return DurationBucket.Over30s;
  }

  if (elapsedMs < 2_000) return DurationBucket.Under2s;
  if (elapsedMs < 5_000) return DurationBucket.From2To5s;
  if (elapsedMs < 10_000) return DurationBucket.From5To10s;
  if (elapsedMs < 30_000) return DurationBucket.From10To30s;
  return DurationBucket.Over30s;
}

/**
 * §9: cuántos movimientos trajo una importación, en tramos.
 *
 * El conteo exacto es un dato del estado de cuenta de esa persona. El tramo
 * contesta "¿importan archivos grandes o pequeños?" sin serlo.
 */
export function toCountBucket(count: number): CountBucketValue {
  if (!Number.isFinite(count) || count <= 0) return CountBucket.Zero;
  if (count <= 10) return CountBucket.OneToTen;
  if (count <= 50) return CountBucket.ElevenToFifty;
  if (count <= 100) return CountBucket.FiftyOneToHundred;
  return CountBucket.HundredOnePlus;
}

/** §5: cuántas fuentes conectadas tiene, en tramos. */
export function toSourceCountBucket(count: number): string {
  if (!Number.isFinite(count) || count <= 0) return SourceCountBucket.Zero;
  if (count === 1) return SourceCountBucket.One;
  if (count === 2) return SourceCountBucket.Two;
  return SourceCountBucket.ThreePlus;
}

/**
 * Tramo de longitud de una transcripción de voz.
 *
 * Sirve para saber si la gente dicta frases cortas ("almuerzo cinco") o
 * párrafos, que es lo que decide si el parser local alcanza. La longitud
 * exacta se queda en el teléfono.
 */
export function toTranscriptLengthBucket(length: number): string {
  if (!Number.isFinite(length) || length <= 0) return 'empty';
  if (length <= 20) return 'under_20';
  if (length <= 50) return '20_50';
  if (length <= 100) return '50_100';
  return 'over_100';
}

/**
 * Extensión del archivo importado, de un conjunto cerrado.
 *
 * Recibe el nombre del archivo y devuelve SOLO el formato: el nombre nunca
 * sale de aquí. Los nombres de estados de cuenta suelen llevar el banco y a
 * veces el número de cuenta enmascarado.
 */
export function toFileFormat(fileName: string | null | undefined): FileFormatValue {
  if (!fileName) return FileFormat.Other;

  const extension = fileName.toLowerCase().split('.').pop();

  switch (extension) {
    case 'xlsx':
      return FileFormat.Xlsx;
    case 'xls':
      return FileFormat.Xls;
    case 'csv':
      return FileFormat.Csv;
    case 'pdf':
      return FileFormat.Pdf;
    default:
      return FileFormat.Other;
  }
}

/**
 * Convierte un valor de enum del backend a un símbolo snake_case.
 *
 * `SpendingPace` -> `spending_pace`. El sanitizador aceptaría igual
 * `spendingpace`, pero un dashboard se lee mucho mejor así, y el valor sigue
 * viniendo de un conjunto cerrado definido en el dominio: nunca de texto
 * escrito por el usuario.
 */
export function toAnalyticsSymbol(value: string | null | undefined): string | undefined {
  if (!value) {
    return undefined;
  }

  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .replace(/[^A-Za-z0-9]+/g, '_')
    .toLowerCase();
}
