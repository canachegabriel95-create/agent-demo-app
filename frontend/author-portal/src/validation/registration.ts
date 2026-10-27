import { passwordRequirements } from './passwordPolicy';

/** The fields of the registration form, named as the API names them. */
export type RegistrationField =
  'invitationCode' | 'fullName' | 'email' | 'password' | 'confirmPassword';

export const REGISTRATION_FIELDS: readonly RegistrationField[] = [
  'invitationCode',
  'fullName',
  'email',
  'password',
  'confirmPassword',
];

export type RegistrationValues = Record<RegistrationField, string>;
export type RegistrationErrors = Partial<Record<RegistrationField, string>>;

export const FULL_NAME_MIN_LENGTH = 2;
export const FULL_NAME_MAX_LENGTH = 100;
export const EMAIL_MAX_LENGTH = 254;

// A bare address: no spaces, one "@", a dot in the host. The API decides what is really valid.
const EMAIL_PATTERN = /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/;

/** The same sentences as the API, so a message does not change when the server repeats the check. */
export const MESSAGES = {
  invitationCodeRequired: 'Invitation code is required.',
  fullNameRequired: 'Full name is required.',
  fullNameLength: `Full name must be between ${FULL_NAME_MIN_LENGTH} and ${FULL_NAME_MAX_LENGTH} characters.`,
  emailRequired: 'Email is required.',
  emailInvalid: `Enter a valid email address of at most ${EMAIL_MAX_LENGTH} characters.`,
  passwordRequired: 'Password is required.',
  passwordPolicy: 'The password does not meet the requirements below.',
  confirmRequired: 'Confirm your password.',
  confirmMismatch: 'Passwords do not match.',
} as const;

/** Validates one field; `undefined` means it is acceptable. */
export function validateField(
  field: RegistrationField,
  values: RegistrationValues,
): string | undefined {
  switch (field) {
    case 'invitationCode':
      return values.invitationCode.trim() ? undefined : MESSAGES.invitationCodeRequired;

    case 'fullName': {
      const name = values.fullName.trim();
      if (!name) {
        return MESSAGES.fullNameRequired;
      }

      return name.length < FULL_NAME_MIN_LENGTH || name.length > FULL_NAME_MAX_LENGTH
        ? MESSAGES.fullNameLength
        : undefined;
    }

    case 'email': {
      const email = values.email.trim();
      if (!email) {
        return MESSAGES.emailRequired;
      }

      return email.length > EMAIL_MAX_LENGTH || !EMAIL_PATTERN.test(email)
        ? MESSAGES.emailInvalid
        : undefined;
    }

    case 'password':
      if (!values.password) {
        return MESSAGES.passwordRequired;
      }

      return passwordRequirements(values.password).every((requirement) => requirement.met)
        ? undefined
        : MESSAGES.passwordPolicy;

    case 'confirmPassword':
      if (!values.confirmPassword) {
        return MESSAGES.confirmRequired;
      }

      // Nothing to compare with yet: the password field reports its own problem.
      return values.password && values.password !== values.confirmPassword
        ? MESSAGES.confirmMismatch
        : undefined;
  }
}

/** Validates every field. */
export function validateRegistration(values: RegistrationValues): RegistrationErrors {
  const errors: RegistrationErrors = {};
  for (const field of REGISTRATION_FIELDS) {
    const message = validateField(field, values);
    if (message) {
      errors[field] = message;
    }
  }

  return errors;
}
