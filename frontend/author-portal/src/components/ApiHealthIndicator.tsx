import { useEffect, useState } from 'react';
import { ApiError, apiFetch } from '../api/client';
import styles from './ApiHealthIndicator.module.css';
import { ErrorState } from './ErrorState';
import { LoadingState } from './LoadingState';

interface HealthCheck {
  name: string;
  status: string;
}

interface HealthReport {
  status: string;
  checks: HealthCheck[];
}

type HealthState =
  { kind: 'loading' } | { kind: 'ok'; report: HealthReport } | { kind: 'down'; error: ApiError };

function isHealthReport(value: unknown): value is HealthReport {
  return (
    typeof value === 'object' &&
    value !== null &&
    'status' in value &&
    'checks' in value &&
    Array.isArray((value as HealthReport).checks)
  );
}

function CheckList({ checks }: { checks: HealthCheck[] }) {
  return (
    <ul className={styles.checks}>
      {checks.map((check) => (
        <li key={check.name}>
          {check.name}: {check.status}
        </li>
      ))}
    </ul>
  );
}

/**
 * Temporary: shows whether the API and its database are reachable. Remove it once the dashboards
 * exist (the home page then shows real content).
 */
export function ApiHealthIndicator() {
  const [state, setState] = useState<HealthState>({ kind: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    apiFetch<HealthReport>('/health', { signal: controller.signal }).then(
      (report) => setState({ kind: 'ok', report }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setState({
          kind: 'down',
          error:
            error instanceof ApiError
              ? error
              : new ApiError({ status: 0, title: 'Unexpected error' }),
        });
      },
    );

    return () => controller.abort();
  }, [attempt]);

  function retry() {
    setState({ kind: 'loading' });
    setAttempt((current) => current + 1);
  }

  return (
    <section className={styles.root} aria-labelledby="api-status-heading">
      <h2 id="api-status-heading" className={styles.heading}>
        API status
      </h2>
      {state.kind === 'loading' && <LoadingState message="Checking the API…" />}
      {state.kind === 'ok' && (
        <>
          <p className={`${styles.summary} ${styles.healthy}`}>API is {state.report.status}</p>
          <CheckList checks={state.report.checks} />
        </>
      )}
      {state.kind === 'down' && (
        <>
          <ErrorState
            title="API unavailable"
            message={
              state.error.status === 0
                ? 'The API cannot be reached. Is it running on the configured address?'
                : 'The API reports a problem.'
            }
            correlationId={state.error.correlationId}
            onRetry={retry}
          />
          {isHealthReport(state.error.body) && <CheckList checks={state.error.body.checks} />}
        </>
      )}
    </section>
  );
}
