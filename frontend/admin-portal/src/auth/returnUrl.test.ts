import { describe, expect, it } from 'vitest';
import { sanitizeReturnUrl } from './returnUrl';

describe('sanitizeReturnUrl', () => {
  it.each([
    ['/', '/'],
    ['/invitations', '/invitations'],
    ['/users?page=2&q=jane', '/users?page=2&q=jane'],
    ['/articles/42#comments', '/articles/42#comments'],
    ['/a/b/c', '/a/b/c'],
  ])('keeps the in-app path %s', (input, expected) => {
    expect(sanitizeReturnUrl(input)).toBe(expected);
  });

  it.each([
    ['https://evil.example/phish'],
    ['http://evil.example'],
    ['//evil.example/phish'],
    ['///evil.example'],
    ['/\\evil.example'],
    ['\\\\evil.example'],
    ['javascript:alert(1)'],
    ['data:text/html,<script>alert(1)</script>'],
    ['evil.example'],
    ['users'],
    ['/path\r\nSet-Cookie: x=1'],
    ['/path\twith-tab'],
    ['/a\\b'],
  ])('falls back to / for %s', (input) => {
    expect(sanitizeReturnUrl(input)).toBe('/');
  });

  it('falls back to / when the value is missing or empty', () => {
    expect(sanitizeReturnUrl(null)).toBe('/');
    expect(sanitizeReturnUrl(undefined)).toBe('/');
    expect(sanitizeReturnUrl('')).toBe('/');
  });

  it('never returns to the login page itself', () => {
    expect(sanitizeReturnUrl('/login')).toBe('/');
    expect(sanitizeReturnUrl('/login?returnUrl=%2Fusers')).toBe('/');
  });

  it('never produces a value that is not a relative path', () => {
    for (const input of ['/ok', '//x', 'http://x', '/\\x', '', 'x']) {
      expect(sanitizeReturnUrl(input).startsWith('/')).toBe(true);
      expect(sanitizeReturnUrl(input).startsWith('//')).toBe(false);
    }
  });
});
