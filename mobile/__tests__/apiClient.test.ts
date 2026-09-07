const mockUpload = jest.fn();

jest.mock('expo-file-system', () => ({
  File: jest.fn().mockImplementation((uri: string) => ({
    uri,
    upload: mockUpload,
  })),
  UploadType: { MULTIPART: 'MULTIPART' },
}));

import { File, UploadType } from 'expo-file-system';
import { configureAuth, requestText, upload } from '../services/apiClient';

const fetchMock = jest.fn();

globalThis.fetch = fetchMock as jest.Mock;

describe('apiClient upload', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    configureAuth(() => null, async () => null);
  });

  it('retries native multipart uploads once after refreshing an expired access token', async () => {
    let accessToken = 'expired-token';
    configureAuth(
      () => ({ accessToken, refreshToken: 'refresh-token' }),
      async () => {
        accessToken = 'fresh-token';
        return { accessToken, refreshToken: 'new-refresh-token' };
      },
    );

    mockUpload
      .mockResolvedValueOnce({ status: 401, body: JSON.stringify({ title: 'Unauthorized' }) })
      .mockResolvedValueOnce({ status: 200, body: JSON.stringify({ importId: 'import-1' }) });

    await expect(
      upload<{ importId: string }>('/api/v1/imports?accountId=account-1', {
        uri: 'file:///statement.csv',
        name: 'statement.csv',
        mimeType: 'text/csv',
      }),
    ).resolves.toEqual({ importId: 'import-1' });

    expect(mockUpload).toHaveBeenCalledTimes(2);
    expect(mockUpload.mock.calls[0]?.[1]?.headers.Authorization).toBe('Bearer expired-token');
    expect(mockUpload.mock.calls[1]?.[1]?.headers.Authorization).toBe('Bearer fresh-token');
  });

  it('sends device and json accept headers with the native multipart body', async () => {
    mockUpload.mockResolvedValueOnce({ status: 200, body: JSON.stringify({ ok: true }) });

    await upload('/api/v1/imports?accountId=account-1', {
      uri: 'file:///statement.xlsx',
      name: 'statement.xlsx',
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });

    expect(File).toHaveBeenCalledWith('file:///statement.xlsx');
    expect(mockUpload.mock.calls[0]?.[0]).toBe('http://localhost:5080/api/v1/imports?accountId=account-1');
    expect(mockUpload.mock.calls[0]?.[1]).toMatchObject({
      httpMethod: 'POST',
      uploadType: UploadType.MULTIPART,
      fieldName: 'file',
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    expect(mockUpload.mock.calls[0]?.[1]?.headers.Accept).toBe('application/json');
    expect(mockUpload.mock.calls[0]?.[1]?.headers['X-Device-Label']).toBeTruthy();
  });

  it('normalizes native upload failures as connection errors', async () => {
    mockUpload.mockRejectedValueOnce(new Error('Cannot open file'));

    const promise = upload('/api/v1/imports?accountId=account-1', {
        uri: 'file:///statement.csv',
        name: 'statement.csv',
        mimeType: 'text/csv',
      });

    await expect(promise).rejects.toThrow('No pudimos conectarnos.');
    await expect(promise).rejects.toMatchObject({ status: 0 });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('apiClient text requests', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    configureAuth(() => ({ accessToken: 'access-token', refreshToken: 'refresh-token' }), async () => null);
  });

  it('downloads authenticated text payloads', async () => {
    fetchMock.mockResolvedValueOnce(new Response('{"exportedAt":"2026-09-06"}', { status: 200 }));

    await expect(requestText('/api/v1/privacy/export')).resolves.toBe('{"exportedAt":"2026-09-06"}');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('http://localhost:5080/api/v1/privacy/export');
    expect(fetchMock.mock.calls[0]?.[1]?.headers.Authorization).toBe('Bearer access-token');
  });
});
