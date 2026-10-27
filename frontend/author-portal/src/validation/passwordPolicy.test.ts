import { describe, expect, it } from 'vitest';
import fixture from '../../../../docs/fixtures/password-cases.json';
import {
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  checkPassword,
  isValidPassword,
  passwordRequirements,
  type PasswordRule,
} from './passwordPolicy';

interface FixtureCase {
  name: string;
  password?: string | null;
  build?: { prefix: string; fill: string; length: number };
  violations: PasswordRule[];
}

const cases = fixture.cases as FixtureCase[];

function passwordOf(testCase: FixtureCase): string | null | undefined {
  if (testCase.build) {
    const { prefix, fill, length } = testCase.build;
    let text = prefix;
    while (text.length < length) {
      text += fill;
    }

    return text.slice(0, length);
  }

  return testCase.password;
}

describe('password policy (shared fixture with the API)', () => {
  it('has the same limits as the API', () => {
    expect(PASSWORD_MIN_LENGTH).toBe(12);
    expect(PASSWORD_MAX_LENGTH).toBe(128);
  });

  it('loads the fixture', () => {
    expect(cases.length).toBeGreaterThan(15);
  });

  it.each(cases.map((testCase) => [testCase.name, testCase] as const))('%s', (_name, testCase) => {
    const password = passwordOf(testCase);

    expect(checkPassword(password)).toEqual(testCase.violations);
    expect(isValidPassword(password)).toBe(testCase.violations.length === 0);
  });

  it('covers every rule', () => {
    const covered = new Set(cases.flatMap((testCase) => testCase.violations));

    expect([...covered].sort()).toEqual([
      'MissingDigit',
      'MissingLowercase',
      'MissingSymbol',
      'MissingUppercase',
      'TooLong',
      'TooShort',
    ]);
  });
});

describe('passwordRequirements', () => {
  it('lists the five requirements, none met for an empty password', () => {
    const requirements = passwordRequirements('');

    expect(requirements.map((requirement) => requirement.id)).toEqual([
      'length',
      'uppercase',
      'lowercase',
      'digit',
      'symbol',
    ]);
    expect(requirements.every((requirement) => !requirement.met)).toBe(true);
  });

  it('marks all met for a valid password', () => {
    expect(passwordRequirements('Str0ng!Passw0rd').every((requirement) => requirement.met)).toBe(
      true,
    );
  });

  it('treats too short and too long as the one length requirement', () => {
    const lengthOf = (password: string) =>
      passwordRequirements(password).find((requirement) => requirement.id === 'length')!.met;

    expect(lengthOf('Aa1!aaaaaaa')).toBe(false);
    expect(lengthOf('Aa1!aaaaaaaa')).toBe(true);
    expect(lengthOf('Aa1!' + 'x'.repeat(124))).toBe(true);
    expect(lengthOf('Aa1!' + 'x'.repeat(125))).toBe(false);
  });

  it('agrees with checkPassword for every fixture case', () => {
    for (const testCase of cases) {
      const password = passwordOf(testCase);
      const unmet = passwordRequirements(password)
        .filter((requirement) => !requirement.met)
        .map((requirement) => requirement.id);
      const expected = new Set(
        testCase.violations.map((rule) =>
          rule === 'TooShort' || rule === 'TooLong'
            ? 'length'
            : rule.replace('Missing', '').toLowerCase(),
        ),
      );

      expect(new Set(unmet), testCase.name).toEqual(expected);
    }
  });
});
