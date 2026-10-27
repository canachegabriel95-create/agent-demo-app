/**
 * The password rules, mirroring `PasswordPolicy` in the API (ContentReview.Domain). The API is the source of
 * truth; both are tested against docs/fixtures/password-cases.json so that they cannot drift apart.
 *
 * Characters are UTF-16 code units, as in .NET: a symbol is anything that is not a letter, a decimal digit or
 * white space.
 */
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;

export type PasswordRule =
  | 'TooShort'
  | 'TooLong'
  | 'MissingUppercase'
  | 'MissingLowercase'
  | 'MissingDigit'
  | 'MissingSymbol';

const UPPERCASE = /^\p{Lu}$/u;
const LOWERCASE = /^\p{Ll}$/u;
const DIGIT = /^\p{Nd}$/u;
const LETTER = /^\p{L}$/u;
const WHITE_SPACE = /^\p{White_Space}$/u;

function someUnit(value: string, test: (unit: string) => boolean): boolean {
  // Deliberately not `for...of`: that walks code points, the server walks UTF-16 code units.
  for (let index = 0; index < value.length; index += 1) {
    if (test(value.charAt(index))) {
      return true;
    }
  }

  return false;
}

/** The violated rules, in the same order as the server; an empty list means the password is acceptable. */
export function checkPassword(password: string | null | undefined): PasswordRule[] {
  const value = password ?? '';
  const violations: PasswordRule[] = [];

  if (value.length < PASSWORD_MIN_LENGTH) {
    violations.push('TooShort');
  }
  if (value.length > PASSWORD_MAX_LENGTH) {
    violations.push('TooLong');
  }
  if (!someUnit(value, (unit) => UPPERCASE.test(unit))) {
    violations.push('MissingUppercase');
  }
  if (!someUnit(value, (unit) => LOWERCASE.test(unit))) {
    violations.push('MissingLowercase');
  }
  if (!someUnit(value, (unit) => DIGIT.test(unit))) {
    violations.push('MissingDigit');
  }
  if (
    !someUnit(value, (unit) => !LETTER.test(unit) && !DIGIT.test(unit) && !WHITE_SPACE.test(unit))
  ) {
    violations.push('MissingSymbol');
  }

  return violations;
}

export function isValidPassword(password: string | null | undefined): boolean {
  return checkPassword(password).length === 0;
}

/** The sentences the API uses for the same rules. */
export const PASSWORD_RULE_TEXT = {
  length: `Must be between ${PASSWORD_MIN_LENGTH} and ${PASSWORD_MAX_LENGTH} characters long.`,
  uppercase: 'Must contain at least one uppercase letter.',
  lowercase: 'Must contain at least one lowercase letter.',
  digit: 'Must contain at least one digit.',
  symbol:
    'Must contain at least one symbol (a character that is not a letter, a digit or white space).',
} as const;

export type PasswordRequirementId = keyof typeof PASSWORD_RULE_TEXT;

export interface PasswordRequirement {
  id: PasswordRequirementId;
  text: string;
  met: boolean;
}

/** One entry per requirement shown to the user (the two length rules are a single requirement). */
export function passwordRequirements(password: string | null | undefined): PasswordRequirement[] {
  const violations = new Set(checkPassword(password));
  const met: Record<PasswordRequirementId, boolean> = {
    length: !violations.has('TooShort') && !violations.has('TooLong'),
    uppercase: !violations.has('MissingUppercase'),
    lowercase: !violations.has('MissingLowercase'),
    digit: !violations.has('MissingDigit'),
    symbol: !violations.has('MissingSymbol'),
  };

  return (Object.keys(PASSWORD_RULE_TEXT) as PasswordRequirementId[]).map((id) => ({
    id,
    text: PASSWORD_RULE_TEXT[id],
    met: met[id],
  }));
}
