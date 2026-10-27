import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PasswordRequirements } from './PasswordRequirements';

describe('PasswordRequirements', () => {
  it('lists every rule as an unmet item for an empty password', () => {
    render(<PasswordRequirements password="" />);

    const items = within(screen.getByTestId('password-requirements')).getAllByRole('listitem');
    expect(items).toHaveLength(5);
    for (const item of items) {
      expect(item).toHaveAttribute('data-met', 'false');
      expect(item).toHaveTextContent('Not met.');
    }
  });

  it('shows met and unmet rules in words, not only by icon or colour', () => {
    render(<PasswordRequirements password="abcdefghijkl" />);

    const byRule = (rule: string) => document.querySelector(`[data-requirement="${rule}"]`)!;
    expect(byRule('length')).toHaveTextContent('Met.');
    expect(byRule('lowercase')).toHaveTextContent('Met.');
    expect(byRule('uppercase')).toHaveTextContent('Not met.');
    expect(byRule('digit')).toHaveTextContent('Not met.');
    expect(byRule('symbol')).toHaveTextContent('Not met.');
  });

  it('updates as the password changes and marks everything met for a valid one', () => {
    const { rerender } = render(<PasswordRequirements password="a" />);
    expect(screen.getAllByText('Not met.', { exact: false })).toHaveLength(4);

    rerender(<PasswordRequirements password="Str0ng!Passw0rd" />);

    for (const item of within(screen.getByTestId('password-requirements')).getAllByRole(
      'listitem',
    )) {
      expect(item).toHaveAttribute('data-met', 'true');
    }
  });

  it('announces changes politely and hides the decorative icons', () => {
    render(<PasswordRequirements password="" id="pw-help" />);

    expect(screen.getByTestId('password-requirements')).toHaveAttribute('aria-live', 'polite');
    expect(document.getElementById('pw-help')).toBeInTheDocument();
    expect(document.querySelector('[aria-hidden="true"]')).toHaveTextContent('○');
  });
});
