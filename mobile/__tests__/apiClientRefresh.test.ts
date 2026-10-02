jest.mock('expo-file-system', () => ({
  File: jest.fn(),
  UploadType: { MULTIPART: 'MULTIPART' },
}));

import { configureAuth, request } from '../services/apiClient';

const fetchMock = jest.fn();
globalThis.fetch = fetchMock as jest.Mock;

function json(status: number, body: unknown) {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

/**
 * BUG "el refresh token no funciona": varias peticiones paralelas recibían 401
 * a la vez y cada una renovaba con el MISMO refresh token. El backend toma la
 * segunda presentación de un token ya rotado como robo y revoca la sesión.
 */
describe('apiClient: renovación de sesión', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('muchas peticiones con 401 al mismo tiempo renuevan UNA sola vez', async () => {
    let accessToken = 'expired';
    const refresher = jest.fn(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
      accessToken = 'fresh';
      return { accessToken, refreshToken: 'rotated' };
    });
    configureAuth(() => ({ accessToken, refreshToken: 'original' }), refresher);

    fetchMock.mockImplementation(async (_url: string, init: { headers: Record<string, string> }) =>
      init.headers.Authorization === 'Bearer fresh' ? json(200, { ok: true }) : json(401, { title: 'Unauthorized' }),
    );

    const results = await Promise.all(
      ['/a', '/b', '/c', '/d', '/e', '/f'].map((path) => request<{ ok: boolean }>(path)),
    );

    expect(results.every((r) => r.ok)).toBe(true);
    expect(refresher).toHaveBeenCalledTimes(1);
  });

  it('un 401 que llega después de que otra petición ya renovó solo reintenta, sin renovar otra vez', async () => {
    let accessToken = 'expired';
    const refresher = jest.fn(async () => ({ accessToken: 'never', refreshToken: 'never' }));
    configureAuth(() => ({ accessToken, refreshToken: 'r' }), refresher);

    fetchMock
      .mockImplementationOnce(async () => {
        // Mientras esta petición viajaba con el token viejo, otra ya renovó.
        accessToken = 'fresh';
        return json(401, {});
      })
      .mockImplementationOnce(async (_url: string, init: { headers: Record<string, string> }) =>
        json(init.headers.Authorization === 'Bearer fresh' ? 200 : 401, { ok: true }),
      );

    await expect(request<{ ok: boolean }>('/x')).resolves.toEqual({ ok: true });
    expect(refresher).not.toHaveBeenCalled();
  });

  it('si la renovación falla, la petición falla una sola vez sin bucles', async () => {
    const refresher = jest.fn(async () => null);
    configureAuth(() => ({ accessToken: 'expired', refreshToken: 'r' }), refresher);
    fetchMock.mockResolvedValue(json(401, { title: 'Unauthorized' }));

    await expect(request('/x')).rejects.toMatchObject({ status: 401 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(refresher).toHaveBeenCalledTimes(1);
  });
});
