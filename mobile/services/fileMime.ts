const MIME_BY_EXTENSION: Record<string, string> = {
  csv: 'text/csv',
  txt: 'text/plain',
  xls: 'application/vnd.ms-excel',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
};

export function mimeTypeForFile(name: string, reportedMimeType?: string | null): string {
  if (reportedMimeType && reportedMimeType.trim().length > 0) {
    return reportedMimeType;
  }

  const extension = name.split('.').pop()?.toLowerCase();
  return extension ? (MIME_BY_EXTENSION[extension] ?? 'application/octet-stream') : 'application/octet-stream';
}
