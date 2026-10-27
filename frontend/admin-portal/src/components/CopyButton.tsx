import { useEffect, useRef, useState, type Ref } from 'react';
import styles from './CopyButton.module.css';

export const COPIED_MESSAGE = 'Copied';
export const COPY_FAILED_MESSAGE = 'Copy failed. Select the text and copy it manually.';

const FEEDBACK_MS = 3000;

export interface CopyButtonProps {
  /** What goes to the clipboard. */
  text: string;
  /** Accessible name, e.g. "Copy code for ann@example.com". Starts with the visible text. */
  label: string;
  testId?: string;
  ref?: Ref<HTMLButtonElement>;
}

/** Copies `text` and announces "Copied" (or the failure) in a polite live region next to the button. */
export function CopyButton({ text, label, testId, ref }: CopyButtonProps) {
  const [feedback, setFeedback] = useState('');
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  async function copy() {
    let message: string;
    try {
      await navigator.clipboard.writeText(text);
      message = COPIED_MESSAGE;
    } catch {
      // No clipboard (insecure context, permission denied): the code stays visible for manual copying.
      message = COPY_FAILED_MESSAGE;
    }

    setFeedback(message);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setFeedback(''), FEEDBACK_MS);
  }

  return (
    <span className={styles.root}>
      <button
        ref={ref}
        type="button"
        className={styles.button}
        aria-label={label}
        data-testid={testId}
        onClick={copy}
      >
        Copy
      </button>
      <span role="status" aria-live="polite" className={styles.feedback}>
        {feedback}
      </span>
    </span>
  );
}
