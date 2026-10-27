const dateTime = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' });

export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso));
}

/** The message for a failed request: the server's explanation when it gave one. */
export function describeError(error: unknown): { message: string; correlationId?: string } {
  if (error instanceof Error && 'status' in error && 'title' in error) {
    const apiError = error as Error & { detail?: string; title: string; correlationId?: string };
    return { message: apiError.detail ?? apiError.title, correlationId: apiError.correlationId };
  }

  return { message: 'Something went wrong. Please try again.' };
}
