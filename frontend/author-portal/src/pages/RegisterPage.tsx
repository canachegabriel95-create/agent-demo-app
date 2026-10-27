import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { register } from '../api/registration';
import { useAuth } from '../auth/useAuth';
import { FormField, PasswordRequirements } from '../components';
import {
  REGISTRATION_FIELDS,
  validateField,
  validateRegistration,
  type RegistrationErrors,
  type RegistrationField,
  type RegistrationValues,
} from '../validation/registration';
import styles from './RegisterPage.module.css';

export const FIX_FIELDS_MESSAGE = 'Please correct the highlighted fields and try again.';
export const TOO_MANY_ATTEMPTS_MESSAGE = 'Too many attempts. Please wait a minute and try again.';
export const GENERIC_FAILURE_MESSAGE = 'Unable to create your account right now. Please try again.';

interface Summary {
  message: string;
  correlationId?: string;
}

const isField = (name: string): name is RegistrationField =>
  (REGISTRATION_FIELDS as readonly string[]).includes(name);

/** Splits a ProblemDetails response into messages for known fields and the rest. */
function mapServerError(error: unknown): { fields: RegistrationErrors; summary: Summary | null } {
  if (!(error instanceof ApiError)) {
    return { fields: {}, summary: { message: GENERIC_FAILURE_MESSAGE } };
  }

  if (error.status === 400 && Object.keys(error.errors).length > 0) {
    const fields: RegistrationErrors = {};
    const other: string[] = [];
    for (const [name, messages] of Object.entries(error.errors)) {
      // The API names fields in camelCase, exactly as this form does.
      if (isField(name)) {
        fields[name] = messages.join(' ');
      } else {
        other.push(...messages);
      }
    }

    return {
      fields,
      summary: { message: other.length > 0 ? other.join(' ') : FIX_FIELDS_MESSAGE },
    };
  }

  if (error.status === 409) {
    // An account with this email already exists.
    return {
      fields: { email: error.detail ?? error.title },
      summary: { message: FIX_FIELDS_MESSAGE },
    };
  }

  if (error.status === 429) {
    return { fields: {}, summary: { message: TOO_MANY_ATTEMPTS_MESSAGE } };
  }

  return {
    fields: {},
    summary: { message: GENERIC_FAILURE_MESSAGE, correlationId: error.correlationId },
  };
}

export function RegisterPage() {
  const { status } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();

  const [values, setValues] = useState<RegistrationValues>(() => ({
    invitationCode: params.get('code')?.trim() ?? '',
    fullName: '',
    email: '',
    password: '',
    confirmPassword: '',
  }));
  const [errors, setErrors] = useState<RegistrationErrors>({});
  const [summary, setSummary] = useState<Summary | null>(null);
  const [failedAttempts, setFailedAttempts] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const summaryRef = useRef<HTMLDivElement>(null);

  // After a failed attempt the summary is what a screen reader user must hear first.
  useEffect(() => {
    if (failedAttempts > 0) {
      summaryRef.current?.focus();
    }
  }, [failedAttempts]);

  if (status === 'authenticated') {
    return <Navigate to="/" replace />;
  }

  function setValue(field: RegistrationField, value: string) {
    const next = { ...values, [field]: value };
    setValues(next);

    // A field that is already marked wrong is re-checked as it is corrected; a clean one waits for blur.
    setErrors((previous) => {
      const updated = { ...previous };
      for (const name of REGISTRATION_FIELDS) {
        const affected = name === field || (field === 'password' && name === 'confirmPassword');
        if (affected && previous[name]) {
          const message = validateField(name, next);
          if (message) {
            updated[name] = message;
          } else {
            delete updated[name];
          }
        }
      }

      return updated;
    });
  }

  function handleBlur(field: RegistrationField) {
    setErrors((previous) => {
      const updated = { ...previous };
      const check = (name: RegistrationField) => {
        const message = validateField(name, values);
        if (message) {
          updated[name] = message;
        } else {
          delete updated[name];
        }
      };

      check(field);
      // Leaving the password re-checks a confirmation that was already typed.
      if (field === 'password' && values.confirmPassword) {
        check('confirmPassword');
      }

      return updated;
    });
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submittingRef.current) {
      return;
    }

    const found = validateRegistration(values);
    setErrors(found);
    if (Object.keys(found).length > 0) {
      setSummary({ message: FIX_FIELDS_MESSAGE });
      setFailedAttempts((count) => count + 1);
      return;
    }

    submittingRef.current = true;
    setSubmitting(true);
    setSummary(null);
    try {
      await register({
        invitationCode: values.invitationCode.trim(),
        fullName: values.fullName.trim(),
        email: values.email.trim(),
        password: values.password,
        confirmPassword: values.confirmPassword,
      });
      navigate('/login?registered=1', { replace: true });
    } catch (failure) {
      const mapped = mapServerError(failure);
      setErrors(mapped.fields);
      setSummary(mapped.summary);
      setFailedAttempts((count) => count + 1);
      submittingRef.current = false;
      setSubmitting(false);
    }
  }

  const field = (name: RegistrationField) => ({
    name,
    value: values[name],
    onChange: (value: string) => setValue(name, value),
    onBlur: () => handleBlur(name),
    error: errors[name],
    disabled: submitting,
    required: true,
    reserveErrorSpace: true,
  });

  return (
    <section className={styles.root} aria-labelledby="register-heading">
      <h1 id="register-heading">Create an account</h1>
      <p className={styles.intro}>
        You need an invitation code from an administrator. Use the email address the invitation was
        issued for.
      </p>

      <form className={styles.form} onSubmit={handleSubmit} noValidate>
        {summary && (
          <div
            ref={summaryRef}
            className={styles.summary}
            role="alert"
            tabIndex={-1}
            data-testid="register-error-summary"
          >
            <p>{summary.message}</p>
            {summary.correlationId && (
              <p className={styles.reference}>
                Reference: <code>{summary.correlationId}</code>
              </p>
            )}
          </div>
        )}
        <FormField
          {...field('invitationCode')}
          label="Invitation code"
          autoComplete="off"
          maxLength={32}
          hint="For example ABCD-EFGH-JKMN. Capital letters and dashes are optional."
          testId="register-invitation-code"
        />
        <FormField
          {...field('fullName')}
          label="Full name"
          autoComplete="name"
          maxLength={100}
          testId="register-full-name"
        />
        <FormField
          {...field('email')}
          label="Email"
          type="email"
          autoComplete="email"
          maxLength={254}
          testId="register-email"
        />
        <FormField
          {...field('password')}
          label="Password"
          type="password"
          autoComplete="new-password"
          maxLength={256}
          describedBy="register-password-requirements"
          testId="register-password"
        />
        <PasswordRequirements password={values.password} id="register-password-requirements" />
        <FormField
          {...field('confirmPassword')}
          label="Confirm password"
          type="password"
          autoComplete="new-password"
          maxLength={256}
          testId="register-confirm-password"
        />
        <button
          type="submit"
          className={styles.submit}
          disabled={submitting}
          data-testid="register-submit"
        >
          {submitting ? 'Creating account…' : 'Create account'}
        </button>
      </form>
      <p className={styles.alternative}>
        <Link to="/login" data-testid="register-login-link">
          Already have an account? Sign in
        </Link>
      </p>
    </section>
  );
}
