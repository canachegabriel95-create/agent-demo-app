import { apiFetch } from './client';

/** An article category. The list is fixed: the API has no way to create, edit or delete one. */
export interface Category {
  id: string;
  name: string;
  slug: string;
}

/** Any signed-in user. The API returns the categories in display order and lets the browser cache them for 5 minutes. */
export function listCategories(signal?: AbortSignal): Promise<Category[]> {
  return apiFetch<Category[]>('/api/categories', { signal });
}
