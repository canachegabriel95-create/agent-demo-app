export type AuthorStatus = 'Active' | 'Suspended';

export interface Author {
  fullName: string;
  email: string;
  status: AuthorStatus;
}

/** One Active and one Suspended author that share a unique tag, so tests never see each other's data. */
export function newAuthors(): { tag: string; active: Author; suspended: Author } {
  const tag = `qa${Date.now()}${Math.floor(Math.random() * 1000)}`;
  return {
    tag,
    active: { fullName: `Active Author ${tag}`, email: `active.${tag}@example.test`, status: 'Active' },
    suspended: { fullName: `Suspended Author ${tag}`, email: `suspended.${tag}@example.test`, status: 'Suspended' },
  };
}
