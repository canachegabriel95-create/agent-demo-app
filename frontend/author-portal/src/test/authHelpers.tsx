import { render } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { appRoutes } from '../app/router';
import { SESSION_KEY } from '../app/config';

export const AUTHOR = {
  id: '22222222-2222-2222-2222-222222222222',
  fullName: 'Jane Author',
  email: 'author@demo.local',
  role: 'Author' as const,
};

export function json(body: unknown, status = 200, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } });
}

export function problem(status: number, title: string) {
  return json({ title, status }, status, 'application/problem+json');
}

export const healthy = () =>
  json({
    status: 'Healthy',
    checks: [
      { name: 'api', status: 'Healthy' },
      { name: 'database', status: 'Healthy' },
    ],
  });

export type Handler = (init: RequestInit | undefined, url: string) => Response | Promise<Response>;

/**
 * Routes the stubbed `fetch` by "METHOD /path". Unhandled requests fail the test loudly instead of hanging.
 */
export function stubApi(handlers: Record<string, Handler>) {
  const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
    const url = new URL(String(input));
    const key = `${init?.method ?? 'GET'} ${url.pathname}`;
    const handler = handlers[key];
    if (!handler) {
      throw new Error(`Unexpected request: ${key}`);
    }

    return handler(init, String(input));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

export function storeSession(token = 'valid-token') {
  window.sessionStorage.setItem(SESSION_KEY, token);
}

export function renderAt(path: string) {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  return router;
}
