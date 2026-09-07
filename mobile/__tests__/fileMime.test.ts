import { mimeTypeForFile } from '../services/fileMime';

describe('mimeTypeForFile', () => {
  it('trusts the MIME type reported by the picker when present', () => {
    expect(mimeTypeForFile('estado.xlsx', 'application/octet-stream')).toBe('application/octet-stream');
  });

  it('infers spreadsheet MIME types when Expo Go omits them', () => {
    expect(mimeTypeForFile('estado.xlsx', undefined)).toBe(
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    );
    expect(mimeTypeForFile('estado.XLS', null)).toBe('application/vnd.ms-excel');
  });

  it('falls back to octet-stream for unknown files', () => {
    expect(mimeTypeForFile('estado.pdf', undefined)).toBe('application/octet-stream');
  });
});
