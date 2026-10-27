import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuditEvent } from '../api/audit';
import {
  ADMIN,
  healthy,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import { detailLines, startOfDay, startOfNextDay } from './AuditPage';

const ACTIONS = [
  'AdministratorProvisioned',
  'InvitationCreated',
  'UserSuspended',
  'ArticleApproved',
];
const ANN = '33333333-3333-3333-3333-333333333333';
const ARTICLE = '44444444-4444-4444-4444-444444444444';

function event(overrides: Partial<AuditEvent> = {}): AuditEvent {
  return {
    id: crypto.randomUUID(),
    occurredAt: '2026-10-27T09:30:00Z',
    action: 'UserSuspended',
    actor: { id: ADMIN.id, fullName: 'Platform Admin', role: 'Administrator' },
    entityType: 'User',
    entityId: ANN,
    details: { reason: 'Posting spam links' },
    correlationId: 'abcdef12-0000-0000-0000-000000000000',
    ...overrides,
  };
}

function page(items: AuditEvent[], totalCount = items.length) {
  return json({ items, page: 1, pageSize: 20, totalCount });
}

let list: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function listCalls() {
  return fetchMock.mock.calls
    .filter(([input]) => new URL(String(input)).pathname === '/api/admin/audit-events')
    .map(([input]) => new URL(String(input)).searchParams);
}

beforeEach(() => {
  storeSession();
  list = () => page([event()]);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function open(path = '/audit') {
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /health': () => healthy(),
    'GET /api/admin/audit-events/actions': () => json(ACTIONS),
    'GET /api/admin/audit-events': (init, url) => list(init, url),
  });
  return renderAt(path);
}

describe('rows', () => {
  it('shows time, action, actor, entity, details and correlation id', async () => {
    open();

    const row = await screen.findByTestId('audit-row');
    expect(within(row).getByText('UserSuspended')).toBeInTheDocument();
    expect(within(row).getByTestId('audit-actor')).toHaveTextContent('Platform Admin');
    expect(within(row).getByRole('link', { name: ANN.slice(-8) })).toHaveAttribute(
      'href',
      `/users/${ANN}`,
    );
    expect(within(row).getByTestId('audit-details')).toHaveTextContent(
      'reason: Posting spam links',
    );
    expect(within(row).getByText('abcdef12')).toBeInTheDocument();
    const time = row.querySelector('time')!;
    expect(time).toHaveAttribute('datetime', '2026-10-27T09:30:00Z');
    expect(time).toHaveAttribute('title', '2026-10-27T09:30:00Z (UTC)');
    expect(listCalls()[0]!.toString()).toBe('');
  });

  it('shows a system event as System and links an article to its review page', async () => {
    list = () =>
      page([
        event({
          actor: null,
          action: 'AdministratorProvisioned',
          correlationId: null,
          details: { email: 'a@b.c' },
        }),
        event({
          action: 'ArticleApproved',
          entityType: 'Article',
          entityId: ARTICLE,
          details: null,
        }),
        event({ action: 'InvitationCreated', entityType: 'Invitation', entityId: ANN }),
      ]);
    open();

    const rows = await screen.findAllByTestId('audit-row');
    expect(within(rows[0]!).getByTestId('audit-actor')).toHaveTextContent('System');
    expect(within(rows[0]!).queryByRole('button', { name: /Copy/ })).not.toBeInTheDocument();
    expect(within(rows[1]!).getByRole('link', { name: ARTICLE.slice(-8) })).toHaveAttribute(
      'href',
      `/reviews/${ARTICLE}`,
    );
    expect(within(rows[1]!).queryByTestId('audit-details')).not.toBeInTheDocument();
    // An invitation has no page of its own: its id is shown, not linked.
    expect(within(rows[2]!).queryByRole('link')).not.toBeInTheDocument();
  });

  it('shows details as text, never as HTML', async () => {
    list = () =>
      page([
        event({
          details: {
            reason: '<img src=x onerror=alert(1)><b>bold</b>',
            note: '<script>window.__pwned=1</script>',
          },
        }),
      ]);
    open();

    const details = await screen.findByTestId('audit-details');

    expect(details).toHaveTextContent('<img src=x onerror=alert(1)><b>bold</b>');
    expect(details.querySelector('img, b, script')).toBeNull();
    expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
  });

  it('has no edit or delete controls', async () => {
    open();

    await screen.findByTestId('audit-row');

    expect(
      screen.queryByRole('button', { name: /edit|delete|remove|revoke|save/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /edit|delete/i })).not.toBeInTheDocument();
  });

  it('formats details as key: value lines', () => {
    expect(detailLines(null)).toEqual([]);
    expect(
      detailLines({ a: 'x', b: 2, c: true, d: null, e: ['p', 'q'], f: { g: 1 }, h: [{ i: 1 }] }),
    ).toEqual(['a: x', 'b: 2', 'c: true', 'd: —', 'e: p, q', 'f: {"g":1}', 'h: [{"i":1}]']);
  });
});

describe('filters', () => {
  it('maps actions, actor, dates and paging to the request and the address', async () => {
    list = () => page([event()], 45);
    const router = open();
    await screen.findByTestId('audit-row');

    await userEvent.click(await screen.findByRole('checkbox', { name: 'UserSuspended' }));
    await waitFor(() => expect(listCalls().at(-1)!.getAll('action')).toEqual(['UserSuspended']));
    await userEvent.click(screen.getByRole('checkbox', { name: 'ArticleApproved' }));
    await waitFor(() =>
      expect(listCalls().at(-1)!.getAll('action')).toEqual(['UserSuspended', 'ArticleApproved']),
    );

    await userEvent.type(screen.getByLabelText('Actor id'), ANN);
    await waitFor(() => expect(listCalls().at(-1)!.get('actorId')).toBe(ANN));

    fireDate('From', '2026-10-01');
    fireDate('To', '2026-10-02');
    await waitFor(() => expect(listCalls().at(-1)!.get('to')).toBe(startOfNextDay('2026-10-02')));
    expect(listCalls().at(-1)!.get('from')).toBe(startOfDay('2026-10-01'));

    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(listCalls().at(-1)!.get('page')).toBe('2'));
    expect(router.state.location.search).toBe(
      `?action=UserSuspended&action=ArticleApproved&actorId=${ANN}&from=2026-10-01&to=2026-10-02&page=2`,
    );

    // Changing a filter starts again at the first page.
    await userEvent.click(screen.getByRole('checkbox', { name: 'ArticleApproved' }));
    await waitFor(() => expect(listCalls().at(-1)!.has('page')).toBe(false));
  });

  it('makes the end date inclusive: the request ends at the start of the next day', () => {
    const next = new Date(startOfNextDay('2026-10-02')).getTime();
    const start = new Date(startOfDay('2026-10-02')).getTime();

    // One calendar day apart (23 to 25 hours around a daylight-saving change).
    expect(next - start).toBeGreaterThanOrEqual(23 * 3600_000);
    expect(next - start).toBeLessThanOrEqual(25 * 3600_000);
  });

  it('applies the address on arrival, as from the dashboard', async () => {
    open(`/audit?action=ArticleApproved&actorId=${ANN}&from=2026-10-01&page=2`);

    await screen.findByTestId('audit-row');

    expect(Object.fromEntries(listCalls().at(-1)!)).toEqual({
      action: 'ArticleApproved',
      actorId: ANN,
      from: startOfDay('2026-10-01'),
      page: '2',
    });
    expect(await screen.findByRole('checkbox', { name: 'ArticleApproved' })).toBeChecked();
    expect(screen.getByLabelText('Actor id')).toHaveValue(ANN);
    expect(screen.getByLabelText('From')).toHaveValue('2026-10-01');
  });

  it('filters by an actor from the table', async () => {
    const router = open();

    await userEvent.click(
      await screen.findByRole('button', { name: 'Only events by Platform Admin' }),
    );

    await waitFor(() => expect(listCalls().at(-1)!.get('actorId')).toBe(ADMIN.id));
    expect(router.state.location.search).toBe(`?actorId=${ADMIN.id}`);
    expect(screen.getByLabelText('Actor id')).toHaveValue(ADMIN.id);
  });

  it('waits for a complete actor id before asking the API', async () => {
    open();
    await screen.findByTestId('audit-row');
    const before = listCalls().length;

    await userEvent.type(screen.getByLabelText('Actor id'), '1234');

    expect(screen.getByText('Enter a valid id.')).toBeInTheDocument();
    expect(listCalls()).toHaveLength(before);
  });

  it('does not ask when the end date is before the start date', async () => {
    open();
    await screen.findByTestId('audit-row');
    const before = listCalls().length;

    fireDate('From', '2026-10-05');
    fireDate('To', '2026-10-01');

    expect(await screen.findByTestId('audit-range-error')).toHaveTextContent('must not be before');
    expect(listCalls().length).toBeLessThanOrEqual(before + 1);
    const last = listCalls().at(-1)!;
    expect(last.has('to') && last.has('from')).toBe(false);
  });

  it('clears every filter', async () => {
    const router = open(
      `/audit?action=ArticleApproved&actorId=${ANN}&from=2026-10-01&to=2026-10-09`,
    );
    await screen.findByTestId('audit-row');

    await userEvent.click(screen.getByTestId('audit-clear'));

    await waitFor(() => expect(router.state.location.search).toBe(''));
    expect(screen.getByLabelText('Actor id')).toHaveValue('');
    expect(screen.getByLabelText('From')).toHaveValue('');
    expect(screen.getByRole('checkbox', { name: 'ArticleApproved' })).not.toBeChecked();
  });
});

describe('states', () => {
  it('says there are no events yet', async () => {
    list = () => page([]);
    open();

    expect(await screen.findByText('No audit events yet')).toBeInTheDocument();
  });

  it('says no events match, and clears the filters', async () => {
    list = (_init, url) => (new URL(url).search ? page([]) : page([event()]));
    const router = open('/audit?action=UserSuspended');

    expect(await screen.findByText('No audit events match the filters')).toBeInTheDocument();
    await userEvent.click(screen.getByTestId('audit-clear-filters'));

    expect(await screen.findByTestId('audit-row')).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
  });

  it('shows loading, then an error with retry', async () => {
    let calls = 0;
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    list = async () => {
      calls += 1;
      if (calls === 1) {
        await gate;
        return problem(500, 'Boom');
      }
      return page([event()]);
    };
    open();

    expect(await screen.findByText('Loading the audit history…')).toBeInTheDocument();
    release();
    expect(await screen.findByText('The audit history could not be loaded')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('audit-row')).toBeInTheDocument();
  });

  it('still works when the action list cannot be loaded', async () => {
    fetchMock = stubApi({
      'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
      'GET /health': () => healthy(),
      'GET /api/admin/audit-events/actions': () => problem(500, 'Boom'),
      'GET /api/admin/audit-events': () => page([event()]),
    });
    renderAt('/audit');

    expect(await screen.findByTestId('audit-row')).toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });
});

describe('navigation', () => {
  it('is reachable from the main navigation', async () => {
    const router = open('/');
    await screen.findByTestId('current-user');

    await userEvent.click(screen.getByRole('link', { name: 'Audit history' }));

    expect(router.state.location.pathname).toBe('/audit');
    expect(await screen.findByRole('heading', { level: 1, name: 'Audit history' })).toBeVisible();
  });
});

function fireDate(label: string, value: string) {
  const input = screen.getByLabelText(label) as HTMLInputElement;
  act(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}
