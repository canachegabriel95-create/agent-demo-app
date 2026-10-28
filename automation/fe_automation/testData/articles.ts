import type { Author } from './authors';

export interface Article {
  title: string;
  summary: string;
  body: string;
  category: string;
}

/** An author with two published articles in different categories, all sharing a unique tag. */
export function newFeedArticles(): { tag: string; author: Author; technology: Article; science: Article } {
  const tag = `qa${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const body = 'This article was created by the automated tests to check the published feed. '.repeat(3);
  return {
    tag,
    author: { fullName: `Feed Author ${tag}`, email: `feed.${tag}@example.test`, status: 'Active' },
    technology: {
      title: `Technology article ${tag}`,
      summary: 'A published technology article for the feed tests.',
      body,
      category: 'Technology',
    },
    science: {
      title: `Science article ${tag}`,
      summary: 'A published science article for the feed tests.',
      body,
      category: 'Science',
    },
  };
}
