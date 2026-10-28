// Signs a QA account in to a portal tab of the QA Chrome without the password ever reaching the caller.
// The credentials are read from .secrets/qa.env and sent only to Chrome over the DevTools protocol;
// this script prints a one-line result and never the password or the session token.
//
// Usage (from automation/):
//   node tools/qa-login.mjs start            start the QA Chrome (debugging port) if it is not running
//   node tools/qa-login.mjs admin [--form]   sign the QA administrator in to the Admin Portal tab
//   node tools/qa-login.mjs author [--form]  sign the QA author in to the Author Portal tab (optional account)
// --form fills and submits the real login form instead of calling the API and storing the token.
// Exit 0 = OK, 1 = login refused or failed, 2 = setup problem (Chrome, secrets file).
import { readFileSync, existsSync, mkdirSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const secretsFile = join(root, '.secrets', 'qa.env');

const PORTALS = {
  admin: { url: 'http://localhost:5173', key: 'cr.admin.session', portal: 'Admin', email: 'QA_ADMIN_EMAIL', password: 'QA_ADMIN_PASSWORD' },
  author: { url: 'http://localhost:5174', key: 'cr.author.session', portal: 'Author', email: 'QA_AUTHOR_EMAIL', password: 'QA_AUTHOR_PASSWORD' },
};

const env = { ...process.env, ...readEnvFile(secretsFile) };
const debugUrl = env.QA_CHROME_DEBUG_URL ?? 'http://127.0.0.1:9222';
const apiBase = env.QA_API_BASE_URL ?? 'http://localhost:5080';

const [command, ...flags] = process.argv.slice(2);
try {
  if (command === 'start') await startChrome();
  else if (PORTALS[command]) await login(PORTALS[command], flags.includes('--form'));
  else fail(2, 'usage: node tools/qa-login.mjs start | admin [--form] | author [--form]');
} catch (e) {
  // Never print e.stack: an evaluated expression could appear in it.
  fail(2, `FAILED ${e.message}`);
}

function fail(code, message) {
  console.log(message);
  process.exit(code);
}

function readEnvFile(path) {
  if (!existsSync(path)) return {};
  const out = {};
  for (const line of readFileSync(path, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/);
    if (m) out[m[1]] = m[2].replace(/^(['"])(.*)\1$/, '$2');
  }
  return out;
}

async function chromeRunning() {
  try {
    return (await fetch(`${debugUrl}/json/version`, { signal: AbortSignal.timeout(1500) })).ok;
  } catch {
    return false;
  }
}

async function startChrome() {
  if (await chromeRunning()) return console.log(`OK QA Chrome already running at ${debugUrl}`);
  const exe = env.QA_CHROME_PATH ?? [
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    join(process.env.LOCALAPPDATA ?? '', 'Google/Chrome/Application/chrome.exe'),
  ].find(existsSync);
  if (!exe) fail(2, 'FAILED Chrome not found; set QA_CHROME_PATH in .secrets/qa.env');
  // Chrome refuses remote debugging on the default profile, so the QA browser has its own.
  const profile = env.QA_CHROME_PROFILE ?? join(process.env.LOCALAPPDATA ?? root, 'content-review-qa', 'chrome-profile');
  mkdirSync(profile, { recursive: true });
  const port = new URL(debugUrl).port || '9222';
  spawn(exe, [`--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check', 'about:blank'],
    { detached: true, stdio: 'ignore' }).unref();
  for (let i = 0; i < 20; i++) {
    await new Promise(r => setTimeout(r, 500));
    if (await chromeRunning()) return console.log(`OK QA Chrome started at ${debugUrl}`);
  }
  fail(2, `FAILED Chrome did not open ${debugUrl}`);
}

async function findOrOpenTab(origin) {
  if (!(await chromeRunning())) await startChrome();
  const targets = await (await fetch(`${debugUrl}/json/list`)).json();
  const tab = targets.find(t => t.type === 'page' && t.url.startsWith(origin));
  if (tab) return tab;
  const res = await fetch(`${debugUrl}/json/new?${origin}/login`, { method: 'PUT' });
  if (!res.ok) fail(2, `FAILED could not open a tab for ${origin}`);
  return res.json();
}

function connect(wsUrl) {
  return new Promise((resolveConn, reject) => {
    const ws = new WebSocket(wsUrl);
    let id = 0;
    const pending = new Map();
    ws.onmessage = ev => {
      const msg = JSON.parse(ev.data);
      if (msg.id && pending.has(msg.id)) {
        pending.get(msg.id)(msg);
        pending.delete(msg.id);
      }
    };
    ws.onerror = () => reject(new Error('cannot connect to the tab'));
    ws.onopen = () => resolveConn({
      send: (method, params = {}) => new Promise(r => {
        pending.set(++id, r);
        ws.send(JSON.stringify({ id, method, params }));
      }),
      close: () => ws.close(),
    });
  });
}

async function evaluate(cdp, expression) {
  const { result, error } = await cdp.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (error) throw new Error('script could not run in the tab');
  if (result.exceptionDetails) throw new Error('script failed in the tab');
  return result.result.value;
}

async function waitForLoad(cdp, origin, path) {
  for (let i = 0; i < 40; i++) {
    await new Promise(r => setTimeout(r, 250));
    const state = await evaluate(cdp, `({ href: location.href, ready: document.readyState })`).catch(() => null);
    if (state?.ready === 'complete' && state.href.startsWith(origin + path)) return;
  }
  throw new Error(`page ${path} did not load`);
}

async function login(p, viaForm) {
  const email = env[p.email];
  const password = env[p.password];
  if (!email || !password) fail(2, `FAILED ${p.email} / ${p.password} missing in .secrets/qa.env`);

  const tab = await findOrOpenTab(p.url);
  const cdp = await connect(tab.webSocketDebuggerUrl);
  const creds = JSON.stringify({ email, password });
  try {
    // A signed-in tab redirects /login to the home page, so drop this portal's session first.
    await cdp.send('Page.navigate', { url: `${p.url}/` });
    await waitForLoad(cdp, p.url, '/');
    await evaluate(cdp, `sessionStorage.removeItem('${p.key}')`);
    await cdp.send('Page.navigate', { url: `${p.url}/login` });
    await waitForLoad(cdp, p.url, '/login');
    let outcome;
    if (viaForm) {
      // Waits for the form, fills the React-controlled inputs and submits; reports only whether a session appeared.
      outcome = await evaluate(cdp, `(async () => {
        const { email, password } = ${creds};
        const find = () => [document.querySelector('input[type=email], input[name=email], input[autocomplete=username]'),
                            document.querySelector('input[type=password]')];
        let [e, pw] = find();
        for (let i = 0; i < 40 && !(e && pw); i++) { await new Promise(r => setTimeout(r, 250)); [e, pw] = find(); }
        if (!e || !pw) return { ok: false, reason: 'login form not found' };
        const set = (el, v) => {
          Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, v);
          el.dispatchEvent(new Event('input', { bubbles: true }));
        };
        set(e, email); set(pw, password);
        const button = pw.form?.querySelector('button[type=submit], button:not([type])');
        if (!button) return { ok: false, reason: 'submit button not found' };
        button.click();
        for (let i = 0; i < 40; i++) {
          await new Promise(r => setTimeout(r, 250));
          if (sessionStorage.getItem('${p.key}')) return { ok: true };
          const err = document.querySelector('[data-testid=login-error], [role=alert]');
          if (err && err.textContent.trim()) return { ok: false, reason: 'form error: ' + err.textContent.trim() };
        }
        return { ok: false, reason: 'no session after submitting the form' };
      })()`);
    } else {
      outcome = await evaluate(cdp, `(async () => {
        const { email, password } = ${creds};
        const r = await fetch('${apiBase}/api/auth/login', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email, password, portal: '${p.portal}' }),
        });
        if (!r.ok) return { ok: false, reason: 'API answered ' + r.status };
        const body = await r.json();
        sessionStorage.setItem('${p.key}', body.token);
        location.href = '/';
        return { ok: true };
      })()`);
    }
    if (!outcome?.ok) fail(1, `FAILED ${p.portal} login: ${outcome?.reason ?? 'unknown'}`);
    console.log(`OK ${p.portal} Portal signed in as ${email} (tab ${tab.id}, ${viaForm ? 'login form' : 'API'})`);
  } finally {
    cdp.close();
  }
}
