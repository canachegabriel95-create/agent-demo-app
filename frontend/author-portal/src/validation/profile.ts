export const FULL_NAME_MIN = 2;
export const FULL_NAME_MAX = 100;
export const BIO_MAX = 1000;

export interface ProfileErrors {
  fullName?: string;
  bio?: string;
}

/** The API's rules and sentences, so the form can refuse a bad value before sending anything. */
export function validateProfile(fullName: string, bio: string): ProfileErrors {
  const name = fullName.trim();
  const errors: ProfileErrors = {};

  if (!name) {
    errors.fullName = 'Full name is required.';
  } else if (name.length < FULL_NAME_MIN || name.length > FULL_NAME_MAX) {
    errors.fullName = `Full name must be between ${FULL_NAME_MIN} and ${FULL_NAME_MAX} characters.`;
  }

  if (bio.trim().length > BIO_MAX) {
    errors.bio = `Biography must be at most ${BIO_MAX} characters.`;
  }

  return errors;
}
