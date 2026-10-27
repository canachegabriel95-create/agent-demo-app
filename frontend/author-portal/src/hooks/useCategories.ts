import { useEffect, useMemo, useState } from 'react';
import { listCategories, type Category } from '../api/categories';

export type CategoriesStatus = 'loading' | 'ready' | 'error';

export interface UseCategories {
  status: CategoriesStatus;
  /** In display order; empty until `status` is `ready`. */
  categories: Category[];
  /** The categories by id, for showing the name of an article's category. */
  byId: ReadonlyMap<string, Category>;
  /** The failure when `status` is `error` (an `ApiError` for API failures). */
  error: unknown;
  /** Loads the list again, e.g. from a "Try again" button. */
  reload: () => void;
}

interface Result {
  attempt: number;
  categories?: Category[];
  error?: unknown;
}

/** Loads the article categories for a filter or the editor. */
export function useCategories(): UseCategories {
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<Result>();

  useEffect(() => {
    const controller = new AbortController();
    listCategories(controller.signal).then(
      (categories) => setResult({ attempt, categories }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ attempt, error });
        }
      },
    );

    return () => controller.abort();
  }, [attempt]);

  // A result that belongs to an earlier attempt is not shown: reloading goes back to "loading".
  const current = result?.attempt === attempt ? result : undefined;
  const categories = useMemo(() => current?.categories ?? [], [current?.categories]);
  const byId = useMemo(
    () => new Map(categories.map((category) => [category.id, category])),
    [categories],
  );

  return {
    status: current?.categories ? 'ready' : current ? 'error' : 'loading',
    categories,
    byId,
    error: current?.error,
    reload: () => setAttempt((value) => value + 1),
  };
}
