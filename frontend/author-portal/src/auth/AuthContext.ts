import { createContext } from 'react';
import type { AuthUser } from '../api/auth';

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthContextValue {
  status: AuthStatus;
  user: AuthUser | null;
  /** True after the API rejected the stored session (expired, revoked or the account was suspended). */
  sessionExpired: boolean;
  /** Signs in; rejects with the API error so the form can show a message. */
  signIn: (email: string, password: string) => Promise<void>;
  /** Ends the session. Never rejects: the local session is cleared even when the API call fails. */
  signOut: () => Promise<void>;
  /** Updates the signed-in user's details after the author changed them (e.g. the display name in the header). */
  updateUser: (changes: Partial<Pick<AuthUser, 'fullName'>>) => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
