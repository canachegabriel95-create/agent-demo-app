import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  AUTHOR,
  healthy,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';

let stats: Handler;

function stub() {
  return stubApi({
    'GET /api/auth/me': () => json({ ...AUTHOR, status: 'Active' }),
    'GET /health': () => healthy(),
    'GET /api/me/articles/stats': (init, url) => stats(init, url),
  });
}

beforeEach(() => {
  storeSession();
  stats = () => json({ draft: 3, pendingReview: 1, published: 5, rejected: 0 });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('dashboard', () => {
  it('shows a count per status, each linking to My Articles filtered by it', async () => {
    stub();
    renderAt('/');

    const draft = await screen.findByTestId('stat-draft');
    expect(draft).toHaveTextContent('3');
    expect(draft).toHaveAttribute('href', '/articles?status=Draft');
    expect(screen.getByTestId('stat-pending-review')).toHaveTextContent('1');
    expect(screen.getByTestId('stat-pending-review')).toHaveAttribute(
      'href',
      '/articles?status=PendingReview',
    );
    expect(screen.getByTestId('stat-published')).toHaveTextContent('5');
    expect(screen.getByTestId('stat-published')).toHaveAttribute(
      'href',
      '/articles?status=Published',
    );
    expect(screen.getByTestId('stat-rejected')).toHaveTextContent('0');
    expect(screen.getByTestId('stat-rejected')).toHaveAttribute(
      'href',
      '/articles?status=Rejected',
    );
  });

  it('shows zeros for statuses without articles and no attention message', async () => {
    stats = () => json({ draft: 0, pendingReview: 0, published: 0, rejected: 0 });
    stub();
    renderAt('/');

    expect(await screen.findByTestId('stat-draft')).toHaveTextContent('0');
    expect(screen.queryByTestId('attention-message')).not.toBeInTheDocument();
  });

  it('says how many articles need attention when some were rejected', async () => {
    stats = () => json({ draft: 0, pendingReview: 0, published: 0, rejected: 2 });
    stub();
    renderAt('/');

    expect(await screen.findByTestId('attention-message')).toHaveTextContent(
      '2 articles need your attention',
    );
  });

  it('uses the singular for one rejected article', async () => {
    stats = () => json({ draft: 0, pendingReview: 0, published: 0, rejected: 1 });
    stub();
    renderAt('/');

    expect(await screen.findByTestId('attention-message')).toHaveTextContent(
      '1 article needs your attention',
    );
  });

  it('offers a New article button that opens the editor', async () => {
    stub();
    const router = renderAt('/');

    await userEvent.click(await screen.findByRole('link', { name: 'New article' }));

    expect(router.state.location.pathname).toBe('/articles/new');
  });

  it('follows a card to My Articles with the status in the URL', async () => {
    stub();
    const router = renderAt('/');

    await userEvent.click(await screen.findByTestId('stat-rejected'));

    expect(router.state.location.pathname).toBe('/articles');
    expect(router.state.location.search).toBe('?status=Rejected');
  });

  it('shows a loading skeleton until the counts arrive', async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    stats = async () => {
      await gate;
      return json({ draft: 1, pendingReview: 0, published: 0, rejected: 0 });
    };
    stub();
    renderAt('/');

    expect(await screen.findByTestId('stats-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('stat-draft')).not.toBeInTheDocument();

    release();
    expect(await screen.findByTestId('stat-draft')).toHaveTextContent('1');
    expect(screen.queryByTestId('stats-loading')).not.toBeInTheDocument();
  });

  it('shows an error and loads the counts again on retry', async () => {
    let calls = 0;
    stats = () => {
      calls += 1;
      return calls === 1
        ? problem(500, 'Boom')
        : json({ draft: 4, pendingReview: 0, published: 0, rejected: 0 });
    };
    stub();
    renderAt('/');

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByTestId('stat-draft')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(screen.getByTestId('stat-draft')).toHaveTextContent('4'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
