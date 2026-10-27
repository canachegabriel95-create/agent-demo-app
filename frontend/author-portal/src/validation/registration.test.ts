import { describe, expect, it } from 'vitest';
import {
  MESSAGES,
  validateField,
  validateRegistration,
  type RegistrationValues,
} from './registration';

const VALID: RegistrationValues = {
  invitationCode: 'ABCD-EFGH-JKMN',
  fullName: 'Jane Doe',
  email: 'jane@demo.local',
  password: 'Str0ng!Passw0rd#9',
  confirmPassword: 'Str0ng!Passw0rd#9',
};

const with_ = (changes: Partial<RegistrationValues>): RegistrationValues => ({
  ...VALID,
  ...changes,
});

describe('registration validation (mirrors the API)', () => {
  it('accepts a complete, valid form', () => {
    expect(validateRegistration(VALID)).toEqual({});
  });

  it('reports every field of an empty form with the API sentences', () => {
    const empty = {
      invitationCode: '',
      fullName: '',
      email: '',
      password: '',
      confirmPassword: '',
    };

    expect(validateRegistration(empty)).toEqual({
      invitationCode: 'Invitation code is required.',
      fullName: 'Full name is required.',
      email: 'Email is required.',
      password: 'Password is required.',
      confirmPassword: 'Confirm your password.',
    });
  });

  it.each([
    ['', MESSAGES.fullNameRequired],
    ['   ', MESSAGES.fullNameRequired],
    ['J', MESSAGES.fullNameLength],
    ['  J  ', MESSAGES.fullNameLength],
    ['n'.repeat(101), MESSAGES.fullNameLength],
    ['Jo', undefined],
    ['n'.repeat(100), undefined],
    ['  Jane Doe  ', undefined],
  ])('full name %j', (fullName, expected) => {
    expect(validateField('fullName', with_({ fullName }))).toBe(expected);
  });

  it.each([
    ['', MESSAGES.emailRequired],
    ['not-an-email', MESSAGES.emailInvalid],
    ['jane@localhost', MESSAGES.emailInvalid],
    ['Jane <jane@demo.local>', MESSAGES.emailInvalid],
    ['two@@demo.local', MESSAGES.emailInvalid],
    [`${'a'.repeat(250)}@demo.local`, MESSAGES.emailInvalid],
    ['jane@demo.local', undefined],
    ['  Jane.Doe+tag@Sub.Demo.LOCAL ', undefined],
  ])('email %j', (email, expected) => {
    expect(validateField('email', with_({ email }))).toBe(expected);
  });

  it('flags a password that breaks any rule, and accepts one that breaks none', () => {
    expect(validateField('password', with_({ password: 'short' }))).toBe(MESSAGES.passwordPolicy);
    expect(validateField('password', with_({ password: 'Str0ng Passw0rd 9' }))).toBe(
      MESSAGES.passwordPolicy,
    );
    expect(validateField('password', with_({ password: 'Aa1!' + 'x'.repeat(125) }))).toBe(
      MESSAGES.passwordPolicy,
    );
    expect(validateField('password', VALID)).toBeUndefined();
  });

  it('requires the confirmation to equal the password exactly', () => {
    expect(validateField('confirmPassword', with_({ confirmPassword: 'Str0ng!Passw0rd#9 ' }))).toBe(
      MESSAGES.confirmMismatch,
    );
    expect(validateField('confirmPassword', with_({ confirmPassword: '' }))).toBe(
      MESSAGES.confirmRequired,
    );
    expect(validateField('confirmPassword', VALID)).toBeUndefined();
  });

  it('does not report a mismatch while no password has been typed', () => {
    expect(validateField('confirmPassword', with_({ password: '', confirmPassword: 'x' }))).toBe(
      undefined,
    );
  });
});
