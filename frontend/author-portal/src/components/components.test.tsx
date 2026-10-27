import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { EmptyState, ErrorState, FormField, LoadingState, Pagination } from '.';

describe('LoadingState', () => {
  it('announces a default loading message politely', () => {
    render(<LoadingState />);

    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Loading…');
    expect(status).toHaveAttribute('aria-live', 'polite');
  });

  it('shows a custom message', () => {
    render(<LoadingState message="Loading articles…" />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading articles…');
  });
});

describe('EmptyState', () => {
  it('shows the title, description and action', () => {
    render(
      <EmptyState
        title="No invitations yet"
        description="Create the first invitation."
        action={<button type="button">Create invitation</button>}
      />,
    );

    expect(screen.getByText('No invitations yet')).toBeInTheDocument();
    expect(screen.getByText('Create the first invitation.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create invitation' })).toBeInTheDocument();
  });

  it('renders only the title when nothing else is given', () => {
    render(<EmptyState title="Nothing here" />);

    expect(screen.getByText('Nothing here')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('ErrorState', () => {
  it('is an alert with the message and the correlation id', () => {
    render(<ErrorState message="The server had a problem." correlationId="corr-42" />);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Something went wrong');
    expect(alert).toHaveTextContent('The server had a problem.');
    expect(alert).toHaveTextContent('Reference: corr-42');
  });

  it('omits the reference when there is no correlation id', () => {
    render(<ErrorState message="Oops" />);

    expect(screen.queryByText(/reference/i)).not.toBeInTheDocument();
  });

  it('offers a retry only when a handler is given', async () => {
    const onRetry = vi.fn();
    const { rerender } = render(<ErrorState message="Oops" />);
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();

    rerender(<ErrorState message="Oops" onRetry={onRetry} />);
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(onRetry).toHaveBeenCalledOnce();
  });
});

describe('Pagination', () => {
  it('shows the current page and disables Previous on the first page', () => {
    render(<Pagination page={1} pageSize={20} totalCount={45} onPageChange={vi.fn()} />);

    expect(screen.getByRole('navigation', { name: 'Pagination' })).toBeInTheDocument();
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next page' })).toBeEnabled();
  });

  it('disables Next on the last page', () => {
    render(<Pagination page={3} pageSize={20} totalCount={45} onPageChange={vi.fn()} />);

    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeEnabled();
  });

  it('reports the requested page', async () => {
    const onPageChange = vi.fn();
    render(<Pagination page={2} pageSize={10} totalCount={50} onPageChange={onPageChange} />);

    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await userEvent.click(screen.getByRole('button', { name: 'Previous page' }));

    expect(onPageChange).toHaveBeenNthCalledWith(1, 3);
    expect(onPageChange).toHaveBeenNthCalledWith(2, 1);
  });

  it('treats an empty list as a single page with both buttons disabled', () => {
    render(<Pagination page={1} pageSize={20} totalCount={0} onPageChange={vi.fn()} />);

    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled();
  });

  it('clamps an out-of-range page', () => {
    render(<Pagination page={99} pageSize={20} totalCount={45} onPageChange={vi.fn()} />);

    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument();
  });
});

describe('FormField reserved error space', () => {
  it('keeps an empty, hidden line where the message will appear', () => {
    const { container, rerender } = render(
      <FormField label="Email" name="email" value="" onChange={vi.fn()} reserveErrorSpace />,
    );
    expect(container.querySelector('p[aria-hidden="true"]')).toBeInTheDocument();

    rerender(
      <FormField
        label="Email"
        name="email"
        value=""
        onChange={vi.fn()}
        reserveErrorSpace
        error="Bad."
      />,
    );

    expect(container.querySelector('p[aria-hidden="true"]')).not.toBeInTheDocument();
    expect(screen.getByText('Bad.')).toBeInTheDocument();
  });

  it('adds nothing unless asked', () => {
    const { container } = render(
      <FormField label="Email" name="email" value="" onChange={vi.fn()} />,
    );

    expect(container.querySelector('p')).not.toBeInTheDocument();
  });
});

describe('FormField', () => {
  function Controlled(props: { error?: string; hint?: string; required?: boolean }) {
    const [value, setValue] = useState('');
    return (
      <FormField label="Full name" name="fullName" value={value} onChange={setValue} {...props} />
    );
  }

  it('labels the input so it can be found by its accessible name', () => {
    render(<Controlled />);

    expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveAttribute('name', 'fullName');
  });

  it('passes typed text to onChange', async () => {
    render(<Controlled />);
    const input = screen.getByRole('textbox', { name: 'Full name' });

    await userEvent.type(input, 'Jane');

    expect(input).toHaveValue('Jane');
  });

  it('links the error message to the input and marks it invalid', () => {
    render(<Controlled error="Full name is required." />);

    const input = screen.getByRole('textbox', { name: 'Full name' });
    const message = screen.getByText('Full name is required.');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('Full name is required.');
    expect(input.getAttribute('aria-describedby')).toContain(message.id);
  });

  it('describes the input with both the hint and the error', () => {
    render(<Controlled hint="2 to 100 characters." error="Too short." />);

    expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveAccessibleDescription(
      '2 to 100 characters. Too short.',
    );
  });

  it('is valid and undescribed without a hint or error', () => {
    render(<Controlled />);

    const input = screen.getByRole('textbox', { name: 'Full name' });
    expect(input).not.toHaveAttribute('aria-invalid');
    expect(input).not.toHaveAttribute('aria-describedby');
  });

  it('marks required fields natively without adding the asterisk to the name', () => {
    render(<Controlled required />);

    const input = screen.getByRole('textbox', { name: 'Full name' });
    expect(input).toBeRequired();
  });

  it('renders a labelled textarea when multiline', () => {
    render(
      <FormField label="Biography" name="bio" value="" onChange={vi.fn()} multiline rows={6} />,
    );

    const textarea = screen.getByRole('textbox', { name: 'Biography' });
    expect(textarea.tagName).toBe('TEXTAREA');
    expect(textarea).toHaveAttribute('rows', '6');
  });

  it('gives every field its own id', () => {
    render(
      <>
        <FormField label="First" name="a" value="" onChange={vi.fn()} />
        <FormField label="Second" name="b" value="" onChange={vi.fn()} />
      </>,
    );

    const first = screen.getByRole('textbox', { name: 'First' });
    const second = screen.getByRole('textbox', { name: 'Second' });
    expect(first.id).not.toBe(second.id);
  });

  it('supports password fields', () => {
    render(<FormField label="Password" name="pw" type="password" value="" onChange={vi.fn()} />);

    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
  });
});
