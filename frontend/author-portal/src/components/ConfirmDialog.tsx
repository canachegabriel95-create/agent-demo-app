import { useRef, useState, type ReactNode } from 'react';
import { Dialog } from './Dialog';
import styles from './ConfirmDialog.module.css';

export interface ConfirmDialogProps {
  title: string;
  /** What is about to happen, in plain words. */
  children: ReactNode;
  confirmLabel: string;
  busyLabel: string;
  confirmTestId: string;
  variant?: 'primary' | 'danger';
  onClose: () => void;
  /**
   * Does the work. Return a message to keep the dialog open and show it; return nothing when the caller has dealt with
   * the outcome (it closes the dialog itself).
   */
  onConfirm: () => Promise<string | undefined>;
}

/** Asks before something that cannot be undone casually. Cancel has the focus, so Enter does not confirm by accident. */
export function ConfirmDialog({
  title,
  children,
  confirmLabel,
  busyLabel,
  confirmTestId,
  variant = 'primary',
  onClose,
  onConfirm,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  async function confirm() {
    setBusy(true);
    setError(undefined);
    const failure = await onConfirm();
    if (failure) {
      setError(failure);
      setBusy(false);
    }
  }

  return (
    <Dialog title={title} onClose={onClose} busy={busy} initialFocus={cancelRef}>
      {children}
      {error && (
        <p className={styles.alert} role="alert">
          {error}
        </p>
      )}
      <div className={styles.actions}>
        <button
          ref={cancelRef}
          type="button"
          className={styles.cancel}
          onClick={onClose}
          disabled={busy}
        >
          Cancel
        </button>
        <button
          type="button"
          className={styles.confirm}
          data-variant={variant}
          onClick={confirm}
          disabled={busy}
          data-testid={confirmTestId}
        >
          {busy ? busyLabel : confirmLabel}
        </button>
      </div>
    </Dialog>
  );
}
