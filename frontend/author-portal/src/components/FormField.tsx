import { useId } from 'react';
import styles from './FormField.module.css';

export interface FormFieldProps {
  label: string;
  name: string;
  value: string;
  onChange: (value: string) => void;
  type?: 'text' | 'email' | 'password' | 'search' | 'number';
  /** Renders a textarea instead of an input. */
  multiline?: boolean;
  rows?: number;
  required?: boolean;
  disabled?: boolean;
  maxLength?: number;
  autoComplete?: string;
  hint?: string;
  /** Validation message. When set the field is marked invalid and the message is linked to it. */
  error?: string;
  /** Called when the field loses focus, e.g. to validate it. */
  onBlur?: () => void;
  /** `data-testid` of the input, for tests where role and name are not enough. */
  testId?: string;
  /** Ids of other elements that describe the field (e.g. a requirements list), added to `aria-describedby`. */
  describedBy?: string;
  /**
   * Keeps one line free for the validation message. Without it a message that appears when the field loses focus
   * pushes the controls below it down, and a click on the submit button that caused the blur misses the button.
   */
  reserveErrorSpace?: boolean;
}

export function FormField({
  label,
  name,
  value,
  onChange,
  type = 'text',
  multiline = false,
  rows = 4,
  required = false,
  disabled = false,
  maxLength,
  autoComplete,
  hint,
  error,
  onBlur,
  testId,
  describedBy: extraDescribedBy,
  reserveErrorSpace = false,
}: FormFieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hint ? hintId : undefined, error ? errorId : undefined, extraDescribedBy]
    .filter(Boolean)
    .join(' ');

  const shared = {
    id,
    name,
    className: styles.input,
    value,
    required,
    disabled,
    maxLength,
    autoComplete,
    'aria-invalid': error ? true : undefined,
    'aria-describedby': describedBy || undefined,
    'data-testid': testId,
    onBlur,
  } as const;

  return (
    <div className={styles.root}>
      <label htmlFor={id} className={styles.label}>
        {label}
        {required && (
          <span className={styles.required} aria-hidden="true">
            {' '}
            *
          </span>
        )}
      </label>
      {multiline ? (
        <textarea {...shared} rows={rows} onChange={(event) => onChange(event.target.value)} />
      ) : (
        <input {...shared} type={type} onChange={(event) => onChange(event.target.value)} />
      )}
      {hint && (
        <p id={hintId} className={styles.hint}>
          {hint}
        </p>
      )}
      {error ? (
        <p id={errorId} className={styles.error}>
          {error}
        </p>
      ) : (
        reserveErrorSpace && <p className={styles.error} aria-hidden="true" />
      )}
    </div>
  );
}
