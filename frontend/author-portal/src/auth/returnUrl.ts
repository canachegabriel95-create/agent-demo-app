const FALLBACK = '/';
const PLACEHOLDER_ORIGIN = 'http://return-url.invalid';

/**
 * Returns a safe in-app path for a `returnUrl` query value, or `/` when the value is missing or could leave the
 * portal (absolute URLs, protocol-relative `//host`, backslash tricks, control characters) or loops back to the
 * login page. Only a relative path with optional query and hash is ever returned.
 */
export function sanitizeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return FALLBACK;
  }

  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f\\]/.test(value)) {
    return FALLBACK;
  }

  let url: URL;
  try {
    url = new URL(value, PLACEHOLDER_ORIGIN);
  } catch {
    return FALLBACK;
  }

  if (url.origin !== PLACEHOLDER_ORIGIN || url.pathname === '/login') {
    return FALLBACK;
  }

  return `${url.pathname}${url.search}${url.hash}`;
}
