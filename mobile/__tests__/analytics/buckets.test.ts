import {
  toCountBucket,
  toDurationBucket,
  toFileFormat,
  toSourceCountBucket,
  toTranscriptLengthBucket,
} from '../../services/analytics/buckets';

describe('buckets de analytics', () => {
  it('agrupa duraciones en tramos', () => {
    expect(toDurationBucket(1_874)).toBe('under_2s');
    expect(toDurationBucket(2_000)).toBe('2_5s');
    expect(toDurationBucket(7_500)).toBe('5_10s');
    expect(toDurationBucket(20_000)).toBe('10_30s');
    expect(toDurationBucket(60_000)).toBe('over_30s');
  });

  it('trata una duración imposible como el tramo más alto', () => {
    expect(toDurationBucket(-1)).toBe('over_30s');
    expect(toDurationBucket(Number.NaN)).toBe('over_30s');
  });

  it('agrupa conteos de movimientos importados', () => {
    expect(toCountBucket(0)).toBe('0');
    expect(toCountBucket(7)).toBe('1_10');
    expect(toCountBucket(50)).toBe('11_50');
    expect(toCountBucket(51)).toBe('51_100');
    expect(toCountBucket(3_400)).toBe('101_plus');
  });

  it('agrupa fuentes conectadas', () => {
    expect(toSourceCountBucket(0)).toBe('0');
    expect(toSourceCountBucket(1)).toBe('1');
    expect(toSourceCountBucket(2)).toBe('2');
    expect(toSourceCountBucket(9)).toBe('3_plus');
  });

  it('agrupa la longitud de una transcripción sin exponerla', () => {
    expect(toTranscriptLengthBucket(0)).toBe('empty');
    expect(toTranscriptLengthBucket(14)).toBe('under_20');
    expect(toTranscriptLengthBucket(35)).toBe('20_50');
    expect(toTranscriptLengthBucket(80)).toBe('50_100');
    expect(toTranscriptLengthBucket(300)).toBe('over_100');
  });

  it('devuelve solo la extensión del archivo, nunca su nombre', () => {
    expect(toFileFormat('EstadoCuenta_Pichincha_2026-01.xlsx')).toBe('xlsx');
    expect(toFileFormat('movimientos.CSV')).toBe('csv');
    expect(toFileFormat('cartola.pdf')).toBe('pdf');
    expect(toFileFormat('archivo.rtf')).toBe('other');
    expect(toFileFormat(null)).toBe('other');
  });
});
