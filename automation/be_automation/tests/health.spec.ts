import { test, expect } from '@playwright/test';

test('GET /health reports Healthy', async ({ request }) => {
  const response = await request.get('/health');
  expect(response.status()).toBe(200);
  expect((await response.json()).status).toBe('Healthy');
});
