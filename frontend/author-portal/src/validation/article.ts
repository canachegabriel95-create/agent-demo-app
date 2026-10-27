export const TITLE_MAX = 150;
export const SUMMARY_MAX = 500;
export const BODY_MAX = 20000;

export interface ArticleDraftErrors {
  title?: string;
  summary?: string;
  body?: string;
}

/** The API's draft rules and sentences: only the title is required, the others only have an upper limit. */
export function validateDraft(title: string, summary: string, body: string): ArticleDraftErrors {
  const errors: ArticleDraftErrors = {};

  if (!title.trim()) {
    errors.title = 'Title is required.';
  } else if (title.trim().length > TITLE_MAX) {
    errors.title = `Title must be at most ${TITLE_MAX} characters.`;
  }

  if (summary.trim().length > SUMMARY_MAX) {
    errors.summary = `Summary must be at most ${SUMMARY_MAX} characters.`;
  }

  if (body.trim().length > BODY_MAX) {
    errors.body = `Body must be at most ${BODY_MAX} characters.`;
  }

  return errors;
}
