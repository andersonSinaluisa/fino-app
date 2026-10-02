import { Platform } from 'react-native';
import * as Device from 'expo-device';
import { File, UploadType } from 'expo-file-system';
import { config } from './config';
import { devLog, serializeError, uriSchemeOf } from './devLog';
import type { ApiProblem } from '../types/api';

/**
 * Entregable 19 ("Sesiones y dispositivos"): the backend has always accepted an
 * X-Device-Label header (RequestContextExtensions.ToRequestContext) to name a
 * refresh token's device, but nothing ever sent it -- every session in the
 * database showed no device name. Computed once: `Device.deviceName` doesn't
 * change while the app is running, and this runs on every request.
 */
const deviceLabel = (
  Device.deviceName ?? (Platform.OS === 'ios' ? 'iPhone' : Platform.OS === 'android' ? 'Android' : 'Web')
).slice(0, 120);

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
  inFlightRefresh = null;
}

/**
 * BUG ("el refresh token no funciona"): Home lanza 6-8 peticiones a la vez
 * (summary, comprometido, presupuestos, pulsos, analytics...). Cuando el
 * access token de 15 minutos vence, TODAS reciben 401 al mismo tiempo y cada
 * una llamaba a refreshTokens() por su cuenta con el MISMO refresh token. El
 * backend rota el refresh token en cada uso y trata la segunda presentación
 * de un token ya rotado como robo (AuthService.RefreshAsync → "reuse
 * detected"): revoca toda la familia de sesiones y responde 401, y la app
 * cerraba la sesión. Es decir: cuanto más paralela la pantalla, más seguro
 * el cierre de sesión al volver a la app después de un rato.
 *
 * Arreglo: una sola renovación en vuelo a la vez (single-flight). Todas las
 * peticiones que reciben 401 mientras tanto esperan esa misma promesa y
 * reintentan con el token nuevo.
 */
let inFlightRefresh: Promise<AuthTokens | null> | null = null;

function refreshOnce(): Promise<AuthTokens | null> {
  if (!inFlightRefresh) {
    inFlightRefresh = refreshTokens().finally(() => {
      inFlightRefresh = null;
    });
  }
  return inFlightRefresh;
}

/**
 * Un 401 con un access token que YA no es el actual significa que otra
 * petición ya renovó la sesión mientras esta viajaba: basta con reintentar,
 * sin gastar (ni quemar) otro refresh token.
 */
async function recoverFromUnauthorized(sentAuthorization: string | undefined): Promise<boolean> {
  const current = getTokens();
  const sentToken = sentAuthorization?.startsWith('Bearer ') ? sentAuthorization.slice(7) : undefined;

  if (current && sentToken && current.accessToken !== sentToken) {
    return true;
  }

  return (await refreshOnce()) !== null;
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

/** Same as parseProblem, for the native upload path below, which gets a body string instead of a Response. */
function parseProblemText(status: number, bodyText: string): ApiProblem {
  if (bodyText.length === 0) {
    return { status };
  }
  try {
    return (JSON.parse(bodyText) as ApiProblem) ?? { status };
  } catch {
    return { status, title: 'Error de red' };
  }
}

function networkProblem(error: unknown): ApiProblem {
  const aborted = error instanceof Error && error.name === 'AbortError';
  return {
    title: aborted ? 'Sin respuesta' : 'Sin conexión',
    detail: aborted
      ? 'El servidor tardó demasiado en responder.'
      : 'No pudimos conectarnos. Revisa tu conexión e inténtalo de nuevo.',
  };
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, authenticated = true, signal, retrying = false } = options;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), config.requestTimeoutMs);
  signal?.addEventListener('abort', () => controller.abort());

  const headers: Record<string, string> = { Accept: 'application/json', 'X-Device-Label': deviceLabel };

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
    throw new ApiError(0, networkProblem(error));
  }

  clearTimeout(timeout);

  // A 401 on an authenticated call means the short-lived access token expired:
  // rotate once, then give up so a revoked session cannot loop.
  if (response.status === 401 && authenticated && !retrying) {
    const refreshed = await recoverFromUnauthorized(headers.Authorization);
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

export async function requestText(path: string, options: RequestOptions = {}): Promise<string> {
  const { method = 'GET', body, authenticated = true, signal, retrying = false } = options;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), config.requestTimeoutMs);
  signal?.addEventListener('abort', () => controller.abort());

  const headers: Record<string, string> = { Accept: 'application/json', 'X-Device-Label': deviceLabel };

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
    throw new ApiError(0, networkProblem(error));
  }

  clearTimeout(timeout);

  if (response.status === 401 && authenticated && !retrying) {
    const refreshed = await recoverFromUnauthorized(headers.Authorization);
    if (refreshed) {
      return requestText(path, { ...options, retrying: true });
    }
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response));
  }

  return response.text();
}

type UploadFile = { uri: string; name: string; mimeType: string; size?: number | null };

/**
 * Multipart upload used by the statement import flow.
 *
 * ROOT CAUSE (found via the devLog trail Anderson pulled off his phone,
 * 2026-09-06): this was never Chrome, iCloud, or a native iOS dialog -- it
 * was this function. `fetch(...) ` on this Expo SDK (React Native's own
 * fetch, called through expo-router's bundle) throws "Unsupported
 * FormDataPart implementation" the moment `form.append('file', { uri, name,
 * type })` is sent to it: the classic React Native idiom of a plain
 * `{ uri, name, type }` object standing in for a Blob is no longer accepted
 * by this fetch's multipart body conversion, which wants an actual
 * Blob/File-like value it recognizes. That crash happened on *every* file
 * regardless of source (Chrome, WhatsApp, wherever) -- WhatsApp "working" in
 * earlier testing was mis-attributed; it was never reaching this far either,
 * it was just failing at a different, earlier point in that particular test.
 *
 * Fix: on iOS/Android, skip fetch/FormData for this request entirely and use
 * expo-file-system's `File.upload()`, which builds the multipart body
 * natively (iOS URLSession / Android's networking stack) from a real local
 * file -- there is no JS-side FormData part for a picky fetch polyfill to
 * reject, and bytes stream from disk instead of loading into memory first.
 * Web keeps the original fetch/FormData path: browsers' own fetch has never
 * had this problem with real Blob/File values.
 */
export async function upload<T>(path: string, file: UploadFile): Promise<T> {
  return uploadOnce<T>(path, file, false);
}

async function uploadOnce<T>(path: string, file: UploadFile, retrying: boolean): Promise<T> {
  const tokens = getTokens();
  const url = `${config.apiBaseUrl}${path}`;
  const headers: Record<string, string> = { Accept: 'application/json', 'X-Device-Label': deviceLabel };
  if (tokens) {
    headers.Authorization = `Bearer ${tokens.accessToken}`;
  }

  devLog('apiClient.upload', 'upload_request_prepared', {
    name: file.name,
    mimeType: file.mimeType,
    uriScheme: uriSchemeOf(file.uri),
    size: file.size ?? null,
    platform: Platform.OS,
  });

  if (Platform.OS === 'web') {
    return uploadViaFetch<T>(path, url, file, headers, retrying);
  }

  let result: { status: number; body: string };
  try {
    const localFile = new File(file.uri);
    result = await localFile.upload(url, {
      httpMethod: 'POST',
      uploadType: UploadType.MULTIPART,
      fieldName: 'file',
      mimeType: file.mimeType,
      headers,
    });
  } catch (uploadError) {
    devLog('apiClient.upload', 'upload_threw', serializeError(uploadError));
    throw new ApiError(0, networkProblem(uploadError));
  }

  devLog('apiClient.upload', 'upload_http_response', { status: result.status });

  if (result.status === 401 && !retrying) {
    const refreshed = await recoverFromUnauthorized(headers.Authorization);
    if (refreshed) {
      return uploadOnce<T>(path, file, true);
    }
  }

  if (result.status < 200 || result.status >= 300) {
    const problem = parseProblemText(result.status, result.body);
    devLog('apiClient.upload', 'http_error_response', { status: result.status, title: problem.title });
    throw new ApiError(result.status, problem);
  }

  return (result.body.length > 0 ? JSON.parse(result.body) : undefined) as T;
}

/**
 * Web's fetch has always accepted a plain `{ uri, name, type }` FormData
 * part without the "Unsupported FormDataPart implementation" problem RN's
 * fetch has -- kept exactly as it worked before for that platform.
 */
async function uploadViaFetch<T>(
  path: string,
  url: string,
  file: UploadFile,
  headers: Record<string, string>,
  retrying: boolean,
): Promise<T> {
  const form = new FormData();

  try {
    form.append('file', { uri: file.uri, name: file.name, type: file.mimeType } as unknown as Blob);
  } catch (formDataError) {
    devLog('apiClient.upload', 'form_data_failed', serializeError(formDataError));
    throw formDataError;
  }

  let response: Response;
  try {
    response = await fetch(url, { method: 'POST', headers, body: form });
  } catch (networkError) {
    devLog('apiClient.upload', 'network_failed', serializeError(networkError));
    throw new ApiError(0, networkProblem(networkError));
  }

  if (response.status === 401 && !retrying) {
    const refreshed = await recoverFromUnauthorized(headers.Authorization);
    if (refreshed) {
      return uploadOnce<T>(path, file, true);
    }
  }

  if (!response.ok) {
    const problem = await parseProblem(response);
    devLog('apiClient.upload', 'http_error_response', { status: response.status, title: problem.title });
    throw new ApiError(response.status, problem);
  }

  return (await response.json()) as T;
}
