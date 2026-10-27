import { describe, expect, it } from 'vitest';
import { validateProfile } from './profile';

const NAME_RULE = 'Full name must be between 2 and 100 characters.';

describe('validateProfile', () => {
  it('accepts a normal profile and an empty biography', () => {
    expect(validateProfile('Ann Author', 'Writes things.')).toEqual({});
    expect(validateProfile('Ann Author', '')).toEqual({});
  });

  it.each([
    ['', 'Full name is required.'],
    ['   ', 'Full name is required.'],
    ['A', NAME_RULE],
    ['  A  ', NAME_RULE],
    ['x'.repeat(101), NAME_RULE],
  ])('rejects the name %j', (name, message) => {
    expect(validateProfile(name, '')).toEqual({ fullName: message });
  });

  it('accepts names of exactly 2 and 100 characters, measured after trimming', () => {
    expect(validateProfile('Al', '')).toEqual({});
    expect(validateProfile(`  ${'x'.repeat(100)}  `, '')).toEqual({});
  });

  it('accepts a biography of exactly 1000 characters and rejects 1001', () => {
    expect(validateProfile('Ann', 'b'.repeat(1000))).toEqual({});
    expect(validateProfile('Ann', 'b'.repeat(1001))).toEqual({
      bio: 'Biography must be at most 1000 characters.',
    });
  });

  it('reports both problems together', () => {
    expect(Object.keys(validateProfile('', 'b'.repeat(1001)))).toEqual(['fullName', 'bio']);
  });
});
