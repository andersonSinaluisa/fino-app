import { config } from './config';
import type { ApiProblem } from '../types/api';

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ApiProblem,
  ) {
    super(problem.detail ?? problem.title ?? 'Algo salió mal.');
    this.name = 'ApiError';
  }

  /** Field-level messages, ready to render next to an input. */
  get fieldErrors(): Record<string, string> {
    const entries = Object.entries(this.problem.errors ?? {});
    return Object.fromEntries(entries.map(([field, messages]) => [field, messages[0] ?? '']));
  }
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
}

type TokenProvider = () => AuthTokens | null;
type TokenRefresher = () => Promise<AuthTokens | null>;

let getTokens: TokenProvider = () => null;
let refreshTokens: TokenRefresher = async () => null;

/**
 * Wired once at startup by the auth store. Keeping the client free of a direct
 * dependency on the store avoids an import cycle and keeps it testable.
 */
export function configureAuth(provider: TokenProvider, refresher: TokenRefresher): void {
  getTokens = provider;
  refreshTokens = refresher;
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  authenticated?: boolean;
  signal?: AbortSignal;
  /** Internal: prevents an endless refresh loop. */
  retrying?: boolean;
}

async function parseProblem(response: Response): Promise<ApiProblem> {
  try {
    const payload = (await response.json()) as ApiProblem;
    return payload ?? { status: response.status };
  } catch {
    return { status: response.status, title: 'Error de red' };
  }
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, authenticated = true, signal, retrying = false } = options;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), config.requestTimeoutMs);
  signal?.addEventListener('abort', () => controller.abort());

  const headers: Record<string, string> = { Accept: 'application/json' };

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (authenticated) {
    const tokens = getTokens();
    if (tokens) {
      headers.Authorization = `Bearer ${tokens.accessToken}`;
    }
  }

  let response: Response;
  try {
    response = await fetch(`${config.apiBaseUrl}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });
  } catch (error) {
    clearTimeout(timeout);
    const aborted = error instanceof Error && error.name === 'AbortError';
    throw new ApiError(0, {
      title: aborted ? 'Sin respuesta' : 'Sin conexión',
      detail: aborted
        ? 'El servidor tardó demasiado en responder.'
        : 'No pudimos conectarnos. Revisa tu conexión e inténtalo de nuevo.',
    });
  }

  clearTimeout(timeout);

  // A 401 on an authenticated call means the short-lived access token expired:
  // rotate once, then give up so a revoked session cannot loop.
  if (response.status === 401 && authenticated && !retrying) {
    const refreshed = await refreshTokens();
    if (refreshed) {
      return request<T>(path, { ...options, retrying: true });
    }
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text.length > 0 ? JSON.parse(text) : undefined) as T;
}

/** Multipart upload used by the statement import flow. */
export async function upload<T>(
  path: string,
  file: { uri: string; name: string; mimeType: string },
): Promise<T> {
  const tokens = getTokens();
  const form = new FormData();

  form.append('file', {
    uri: file.uri,
    name: file.name,
    type: file.mimeType,
  } as unknown as Blob);

  const response = await fetch(`${config.apiBaseUrl}${path}`, {
    method: 'POST',
    headers: tokens ? { Authorization: `Bearer ${tokens.accessToken}`, Accept: 'application/json' } : {},
    body: form,
  });

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response));
  }

  return (await response.json()) as T;
}
