import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, apiFetch, setUnauthorizedHandler } from './client';
import { clearToken, getToken, setToken } from './session';

const fetchMock = vi.fn<typeof fetch>();

function json(body: unknown, init: ResponseInit = {}, contentType = 'application/json') {
  return new Response(JSON.stringify(body), {
    ...init,
    headers: { 'Content-Type': contentType, ...(init.headers as Record<string, string>) },
  });
}

function lastCall() {
  const [url, init] = fetchMock.mock.calls.at(-1)!;
  return { url: String(url), init: init!, headers: new Headers(init!.headers) };
}

async function failure(promise: Promise<unknown>): Promise<ApiError> {
  const error = await promise.then(
    () => undefined,
    (e: unknown) => e,
  );
  expect(error).toBeInstanceOf(ApiError);
  return error as ApiError;
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  clearToken();
});

describe('apiFetch requests', () => {
  it('prefixes the base URL and returns the parsed JSON body', async () => {
    fetchMock.mockResolvedValue(json({ status: 'Healthy' }));

    const result = await apiFetch<{ status: string }>('/health');

    expect(result).toEqual({ status: 'Healthy' });
    expect(lastCall().url).toBe('http://localhost:5080/health');
  });

  it('uses VITE_API_BASE_URL and tolerates trailing and missing slashes', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test/');
    fetchMock.mockResolvedValue(json({}));

    await apiFetch('api/articles');

    expect(lastCall().url).toBe('https://api.example.test/api/articles');
  });

  it('asks for JSON and sends a JSON body with the right content type', async () => {
    fetchMock.mockResolvedValue(json({ id: 1 }, { status: 201 }));

    await apiFetch('/api/things', { method: 'POST', body: { name: 'x', count: 2 } });

    const { init, headers } = lastCall();
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"name":"x","count":2}');
    expect(headers.get('Accept')).toBe('application/json');
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('does not set a content type when there is no body', async () => {
    fetchMock.mockResolvedValue(json({}));

    await apiFetch('/api/things');

    expect(lastCall().headers.has('Content-Type')).toBe(false);
    expect(lastCall().init.body).toBeUndefined();
  });

  it('attaches the bearer token from session storage when present', async () => {
    setToken('abc123');
    fetchMock.mockResolvedValue(json({}));

    await apiFetch('/api/me');

    expect(lastCall().headers.get('Authorization')).toBe('Bearer abc123');
  });

  it('sends no Authorization header without a token', async () => {
    fetchMock.mockResolvedValue(json({}));

    await apiFetch('/api/me');

    expect(lastCall().headers.has('Authorization')).toBe(false);
  });

  it('keeps caller headers and lets them add to the defaults', async () => {
    fetchMock.mockResolvedValue(json({}));

    await apiFetch('/api/things', { headers: { 'X-Test': '1' } });

    expect(lastCall().headers.get('X-Test')).toBe('1');
    expect(lastCall().headers.get('Accept')).toBe('application/json');
  });

  it('returns undefined for 204 No Content', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await expect(apiFetch('/api/things/1', { method: 'DELETE' })).resolves.toBeUndefined();
  });

  it('passes an abort signal through and rethrows aborts untouched', async () => {
    const abort = new DOMException('Aborted', 'AbortError');
    fetchMock.mockRejectedValue(abort);

    await expect(apiFetch('/health', { signal: new AbortController().signal })).rejects.toBe(abort);
  });
});

describe('apiFetch errors', () => {
  it('converts ProblemDetails into a typed ApiError with the correlation id', async () => {
    fetchMock.mockResolvedValue(
      json(
        {
          type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
          title: 'Conflict',
          status: 409,
          detail: 'The article was changed by someone else.',
          correlationId: 'corr-42',
        },
        { status: 409, statusText: 'Conflict' },
        'application/problem+json',
      ),
    );

    const error = await failure(apiFetch('/api/articles/1', { method: 'PUT', body: {} }));

    expect(error.status).toBe(409);
    expect(error.title).toBe('Conflict');
    expect(error.detail).toBe('The article was changed by someone else.');
    expect(error.correlationId).toBe('corr-42');
    expect(error.errors).toEqual({});
    expect(error.message).toBe('The article was changed by someone else.');
    expect(error.name).toBe('ApiError');
  });

  it('exposes validation errors keyed by field', async () => {
    fetchMock.mockResolvedValue(
      json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { email: ['Email is required.'], fullName: ['Too short.', 'Required.'] },
          correlationId: 'corr-1',
        },
        { status: 400 },
        'application/problem+json',
      ),
    );

    const error = await failure(apiFetch('/api/users', { method: 'POST', body: {} }));

    expect(error.status).toBe(400);
    expect(error.errors).toEqual({
      email: ['Email is required.'],
      fullName: ['Too short.', 'Required.'],
    });
  });

  it('falls back to the X-Correlation-Id header when the body has none', async () => {
    fetchMock.mockResolvedValue(
      json(
        { title: 'Not Found', status: 404 },
        { status: 404, headers: { 'X-Correlation-Id': 'from-header' } },
        'application/problem+json',
      ),
    );

    const error = await failure(apiFetch('/api/articles/missing'));

    expect(error.correlationId).toBe('from-header');
  });

  it('prefers the body correlation id over the header', async () => {
    fetchMock.mockResolvedValue(
      json(
        { title: 'Boom', status: 500, correlationId: 'from-body' },
        { status: 500, headers: { 'X-Correlation-Id': 'from-header' } },
      ),
    );

    const error = await failure(apiFetch('/api/anything'));

    expect(error.correlationId).toBe('from-body');
  });

  it('copes with non-JSON error responses', async () => {
    fetchMock.mockResolvedValue(
      new Response('<html>Bad gateway</html>', {
        status: 502,
        statusText: 'Bad Gateway',
        headers: { 'Content-Type': 'text/html' },
      }),
    );

    const error = await failure(apiFetch('/api/anything'));

    expect(error.status).toBe(502);
    expect(error.title).toBe('Bad Gateway');
    expect(error.detail).toBeUndefined();
    expect(error.body).toBeUndefined();
  });

  it('copes with a malformed JSON error body', async () => {
    fetchMock.mockResolvedValue(
      new Response('{not json', { status: 500, headers: { 'Content-Type': 'application/json' } }),
    );

    const error = await failure(apiFetch('/api/anything'));

    expect(error.status).toBe(500);
    expect(error.title).toBe('Request failed');
  });

  it('keeps the parsed body so callers can read non-ProblemDetails payloads (e.g. /health 503)', async () => {
    const payload = { status: 'Unhealthy', checks: [{ name: 'database', status: 'Unhealthy' }] };
    fetchMock.mockResolvedValue(json(payload, { status: 503, statusText: 'Service Unavailable' }));

    const error = await failure(apiFetch('/health'));

    expect(error.status).toBe(503);
    expect(error.title).toBe('Service Unavailable');
    expect(error.body).toEqual(payload);
  });

  it('turns a network failure into an ApiError with status 0', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const error = await failure(apiFetch('/health'));

    expect(error.status).toBe(0);
    expect(error.title).toBe('Network error');
    expect(error.detail).toMatch(/unable to reach the server/i);
  });
});

describe('apiFetch 401 handling', () => {
  it('clears the session and runs the unauthorized handler, then still throws', async () => {
    setToken('stale');
    const handler = vi.fn();
    const restore = setUnauthorizedHandler(handler);
    fetchMock.mockResolvedValue(
      json({ title: 'Unauthorized', status: 401 }, { status: 401 }, 'application/problem+json'),
    );

    try {
      const error = await failure(apiFetch('/api/me'));
      expect(error.status).toBe(401);
      expect(handler).toHaveBeenCalledOnce();
    } finally {
      restore();
    }
  });

  it('by default clears the stored token and redirects to /login', async () => {
    setToken('stale');
    const assign = vi.fn();
    vi.stubGlobal('location', { pathname: '/articles', assign });
    fetchMock.mockResolvedValue(json({ title: 'Unauthorized', status: 401 }, { status: 401 }));

    await failure(apiFetch('/api/me'));

    expect(getToken()).toBeNull();
    expect(assign).toHaveBeenCalledWith('/login');
  });

  it('does not redirect when already on /login (wrong credentials also return 401)', async () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { pathname: '/login', assign });
    fetchMock.mockResolvedValue(json({ title: 'Unauthorized', status: 401 }, { status: 401 }));

    await failure(apiFetch('/api/auth/login', { method: 'POST', body: {} }));

    expect(assign).not.toHaveBeenCalled();
  });

  it('does not run the handler for other error statuses', async () => {
    const handler = vi.fn();
    const restore = setUnauthorizedHandler(handler);
    fetchMock.mockResolvedValue(json({ title: 'Forbidden', status: 403 }, { status: 403 }));

    try {
      await failure(apiFetch('/api/admin'));
      expect(handler).not.toHaveBeenCalled();
    } finally {
      restore();
    }
  });
});
