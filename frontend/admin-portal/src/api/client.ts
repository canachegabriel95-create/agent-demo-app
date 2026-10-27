import { getApiBaseUrl } from '../app/config';
import { clearToken, getToken } from './session';

/** RFC 9457 ProblemDetails as returned by the API (plus the `correlationId` extension). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  /** Validation errors keyed by camelCase field name. */
  errors?: Record<string, string[]>;
  correlationId?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string | undefined;
  readonly errors: Record<string, string[]>;
  readonly correlationId: string | undefined;
  /** The parsed JSON response body, when there was one (e.g. the `/health` payload on a 503). */
  readonly body: unknown;

  constructor(init: {
    status: number;
    title: string;
    detail?: string;
    errors?: Record<string, string[]>;
    correlationId?: string;
    body?: unknown;
  }) {
    super(init.detail ?? init.title);
    this.name = 'ApiError';
    this.status = init.status;
    this.title = init.title;
    this.detail = init.detail;
    this.errors = init.errors ?? {};
    this.correlationId = init.correlationId;
    this.body = init.body;
  }
}

export interface ApiFetchOptions extends Omit<RequestInit, 'body'> {
  /** Serialized as JSON when defined. */
  body?: unknown;
}

type UnauthorizedHandler = () => void;

/** Default 401 behavior: forget the session and go to the login page (unless already there). */
function redirectToLogin(): void {
  clearToken();
  if (window.location.pathname !== '/login') {
    window.location.assign('/login');
  }
}

let unauthorizedHandler: UnauthorizedHandler = redirectToLogin;

/** Overrides what happens on a 401 response. Returns a function that restores the previous handler. */
export function setUnauthorizedHandler(handler: UnauthorizedHandler): () => void {
  const previous = unauthorizedHandler;
  unauthorizedHandler = handler;
  return () => {
    unauthorizedHandler = previous;
  };
}

function buildUrl(path: string): string {
  const base = getApiBaseUrl().replace(/\/+$/, '');
  return `${base}${path.startsWith('/') ? path : `/${path}`}`;
}

async function readJson(response: Response): Promise<unknown> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!/json/i.test(contentType)) {
    return undefined;
  }

  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

function toApiError(response: Response, body: unknown): ApiError {
  const problem: ProblemDetails = typeof body === 'object' && body !== null ? body : {};
  const title =
    typeof problem.title === 'string' && problem.title
      ? problem.title
      : response.statusText || 'Request failed';

  return new ApiError({
    status: response.status,
    title,
    detail: typeof problem.detail === 'string' ? problem.detail : undefined,
    errors: problem.errors && typeof problem.errors === 'object' ? problem.errors : undefined,
    correlationId:
      (typeof problem.correlationId === 'string' ? problem.correlationId : undefined) ??
      response.headers.get('X-Correlation-Id') ??
      undefined,
    body,
  });
}

/**
 * Calls the API: prefixes `VITE_API_BASE_URL`, sends/receives JSON, attaches the bearer token and
 * turns every failure into a typed {@link ApiError}.
 */
export async function apiFetch<T>(path: string, options: ApiFetchOptions = {}): Promise<T> {
  const { body, headers: extraHeaders, ...init } = options;

  const headers = new Headers(extraHeaders);
  headers.set('Accept', 'application/json');
  if (body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }

  const token = getToken();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  let response: Response;
  try {
    response = await fetch(buildUrl(path), {
      ...init,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }

    throw new ApiError({
      status: 0,
      title: 'Network error',
      detail: 'Unable to reach the server. Check your connection and try again.',
    });
  }

  if (response.ok) {
    if (response.status === 204) {
      return undefined as T;
    }

    return (await readJson(response)) as T;
  }

  const error = toApiError(response, await readJson(response));
  // A 401 only means "your session ended" when a session was sent; a wrong password at login also returns 401.
  if (response.status === 401 && token) {
    unauthorizedHandler();
  }

  throw error;
}
