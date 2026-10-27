import { apiFetch } from './client';

export interface RegistrationInput {
  invitationCode: string;
  fullName: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export interface RegisteredUser {
  id: string;
  email: string;
  fullName: string;
}

/**
 * Creates an Author account with an invitation code. No session is created: the person signs in afterwards.
 * A 400 carries `errors` keyed by these field names; 409 means the email already has an account.
 */
export function register(input: RegistrationInput, signal?: AbortSignal): Promise<RegisteredUser> {
  return apiFetch<RegisteredUser>('/api/registration', { method: 'POST', body: input, signal });
}
