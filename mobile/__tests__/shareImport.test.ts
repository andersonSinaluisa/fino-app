import { ACCEPTED_EXTENSIONS, extensionOf, guessMimeType } from '../lib/importFileTypes';
import { clearPendingSharedFile, readPendingSharedFile } from '../lib/shareImport';

describe('extensionOf / guessMimeType (shared by the manual picker and the share-sheet hand-off)', () => {
  it('lowercases and strips the extension', () => {
    expect(extensionOf('Extracto Banco.CSV')).toBe('csv');
    expect(extensionOf('movimientos.XLSX')).toBe('xlsx');
    expect(extensionOf('sin-extension')).toBeNull();
  });

  it('maps every extension the importer accepts to a real MIME type', () => {
    expect(ACCEPTED_EXTENSIONS).toEqual(['csv', 'xls', 'xlsx', 'txt']);
    expect(guessMimeType('extracto.csv')).toBe('text/csv');
    expect(guessMimeType('extracto.xls')).toBe('application/vnd.ms-excel');
    expect(guessMimeType('extracto.xlsx')).toBe(
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    );
    expect(guessMimeType('extracto.txt')).toBe('text/plain');
  });

  it('falls back to a generic type for anything unrecognized, matching resolvePickedFile leaving the real gate to the extension check', () => {
    expect(guessMimeType('extracto.pdf')).toBe('application/octet-stream');
    expect(guessMimeType('sin-extension')).toBe('application/octet-stream');
  });
});

describe('lib/shareImport fallback (any platform with no share-sheet integration, e.g. web)', () => {
  it('never resolves a pending shared file', async () => {
    await expect(readPendingSharedFile()).resolves.toBeNull();
    await expect(readPendingSharedFile('extracto.csv')).resolves.toBeNull();
  });

  it('clearPendingSharedFile is a harmless no-op', () => {
    expect(() => clearPendingSharedFile()).not.toThrow();
  });
});
