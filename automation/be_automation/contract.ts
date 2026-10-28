import { expect, type APIResponse } from '@playwright/test';

export async function expectProblem(response: APIResponse, status: number) {
  expect(response.status()).toBe(status);
  expect(response.headers()['content-type']).toContain('application/problem+json');
  const body = await response.json();
  expect(body.status).toBe(status);
  expect(body.correlationId).toBe(response.headers()['x-correlation-id']);
  return body;
}

export async function expectPage(response: APIResponse) {
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(Array.isArray(body.items)).toBe(true);
  for (const key of ['page', 'pageSize', 'totalCount']) expect(typeof body[key]).toBe('number');
  return body;
}
