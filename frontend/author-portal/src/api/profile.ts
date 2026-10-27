import { apiFetch } from './client';

/** The signed-in author's own account details. Email and role are read-only. */
export interface Profile {
  id: string;
  fullName: string;
  email: string;
  bio: string | null;
  role: 'Author';
  registeredAt: string;
  /** Concurrency token: send it back when saving. */
  version: number;
}

export function getProfile(signal?: AbortSignal): Promise<Profile> {
  return apiFetch<Profile>('/api/me/profile', { signal });
}

/**
 * Saves the display name and the biography. A 400 carries `errors.fullName` / `errors.bio`; a stale `version` is a 409.
 * Email and role cannot be changed.
 */
export function updateProfile(
  profile: { fullName: string; bio: string },
  version: number,
): Promise<Profile> {
  return apiFetch<Profile>('/api/me/profile', {
    method: 'PUT',
    body: { fullName: profile.fullName, bio: profile.bio, version },
  });
}
