import styles from './ErrorState.module.css';

export interface ErrorStateProps {
  message: string;
  title?: string;
  /** Shown so that a user can quote it to support and a developer can find the request in the logs. */
  correlationId?: string;
  onRetry?: () => void;
}

export function ErrorState({
  message,
  title = 'Something went wrong',
  correlationId,
  onRetry,
}: ErrorStateProps) {
  return (
    <div className={styles.root} role="alert">
      <p className={styles.title}>{title}</p>
      <p className={styles.message}>{message}</p>
      {correlationId && (
        <p className={styles.reference}>
          Reference: <code>{correlationId}</code>
        </p>
      )}
      {onRetry && (
        <button type="button" className={styles.retry} onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}
