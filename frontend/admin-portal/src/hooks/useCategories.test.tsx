import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import type { Category } from '../api/categories';
import { json, problem, stubApi, type Handler } from '../test/authHelpers';
import { useCategories } from './useCategories';

const CATEGORIES: Category[] = [
  { id: 'c-1', name: 'Technology', slug: 'technology' },
  { id: 'c-2', name: 'Science', slug: 'science' },
  { id: 'c-3', name: 'Business', slug: 'business' },
];

let list: Handler;
let fetchMock: ReturnType<typeof stubApi>;

beforeEach(() => {
  list = () => json(CATEGORIES);
  fetchMock = stubApi({ 'GET /api/categories': (init, url) => list(init, url) });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('useCategories', () => {
  it('starts loading with an empty list, then returns the categories in the order the API gave', async () => {
    const { result } = renderHook(() => useCategories());

    expect(result.current.status).toBe('loading');
    expect(result.current.categories).toEqual([]);

    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.categories.map((category) => category.name)).toEqual([
      'Technology',
      'Science',
      'Business',
    ]);
    expect(result.current.error).toBeUndefined();
  });

  it('offers the categories by id', async () => {
    const { result } = renderHook(() => useCategories());
    await waitFor(() => expect(result.current.status).toBe('ready'));

    expect(result.current.byId.get('c-2')?.slug).toBe('science');
    expect(result.current.byId.get('missing')).toBeUndefined();
    expect(result.current.byId.size).toBe(3);
  });

  it('asks the API once per mount and not again on re-render', async () => {
    const { result, rerender } = renderHook(() => useCategories());
    await waitFor(() => expect(result.current.status).toBe('ready'));

    rerender();
    rerender();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('reports an API failure as an error with the failure and no categories', async () => {
    list = () => problem(500, 'Server error');
    const { result } = renderHook(() => useCategories());

    await waitFor(() => expect(result.current.status).toBe('error'));

    expect(result.current.categories).toEqual([]);
    expect(result.current.error).toBeInstanceOf(ApiError);
    expect((result.current.error as ApiError).status).toBe(500);
  });

  it('goes back to loading on reload and then shows the new result', async () => {
    list = () => problem(500, 'Server error');
    const { result } = renderHook(() => useCategories());
    await waitFor(() => expect(result.current.status).toBe('error'));

    list = () => json(CATEGORIES);
    act(() => result.current.reload());

    expect(result.current.status).toBe('loading');
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.categories).toHaveLength(3);
    expect(result.current.error).toBeUndefined();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not report a failure after the component is gone', async () => {
    let fail!: () => void;
    list = () =>
      new Promise((resolve) => {
        fail = () => resolve(problem(500, 'Server error'));
      });
    const { result, unmount } = renderHook(() => useCategories());
    expect(result.current.status).toBe('loading');

    unmount();
    fail();

    // Nothing to assert on a dead hook except that this did not throw an unhandled rejection or a state warning.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(result.current.status).toBe('loading');
  });
});
