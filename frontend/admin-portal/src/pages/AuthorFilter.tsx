import { useEffect, useId, useState } from 'react';
import { lookupAuthors, type AuthorLookupItem } from '../api/reviews';
import styles from './ReviewQueuePage.module.css';

export const LOOKUP_DEBOUNCE_MS = 250;

export interface AuthorFilterProps {
  /** The selected author's id (from the address), or empty. */
  authorId: string;
  /** Name of the selected author, when known; shown instead of the id. */
  authorName?: string;
  onSelect: (author: AuthorLookupItem) => void;
  onClear: () => void;
}

/**
 * Finds an author by typing part of the name or email. Matches (at most 20) are offered as a list; choosing one
 * filters the queue and shows the author as a removable chip.
 */
export function AuthorFilter({ authorId, authorName, onSelect, onClear }: AuthorFilterProps) {
  const inputId = useId();
  const listId = useId();
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  const [matches, setMatches] = useState<AuthorLookupItem[]>([]);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const term = text.trim();
    if (!open || term === '') {
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      lookupAuthors(term, controller.signal).then(
        (found) => {
          setMatches(found);
          setFailed(false);
        },
        () => {
          if (!controller.signal.aborted) {
            setFailed(true);
          }
        },
      );
    }, LOOKUP_DEBOUNCE_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [text, open]);

  if (authorId) {
    return (
      <div className={styles.filter}>
        <span className={styles.filterLabel} id={`${inputId}-selected`}>
          Author
        </span>
        <span
          className={styles.chip}
          data-testid="review-author-chip"
          aria-labelledby={`${inputId}-selected`}
        >
          {authorName ?? 'Selected author'}
          <button
            type="button"
            className={styles.chipClear}
            onClick={() => {
              setText('');
              setMatches([]);
              onClear();
            }}
            aria-label={`Clear the author filter${authorName ? ` (${authorName})` : ''}`}
            data-testid="review-author-clear"
          >
            ×
          </button>
        </span>
      </div>
    );
  }

  const term = text.trim();
  return (
    <div className={styles.filter}>
      <label htmlFor={inputId} className={styles.filterLabel}>
        Author
      </label>
      <input
        id={inputId}
        type="search"
        className={styles.input}
        placeholder="Name or email"
        autoComplete="off"
        role="combobox"
        aria-expanded={open && term !== ''}
        aria-controls={listId}
        aria-autocomplete="list"
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setOpen(true);
        }}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        data-testid="review-author-search"
      />
      {open && term !== '' && (
        <ul id={listId} className={styles.suggestions} role="listbox" aria-label="Matching authors">
          {failed ? (
            <li className={styles.suggestionNote}>The authors could not be loaded.</li>
          ) : matches.length === 0 ? (
            <li className={styles.suggestionNote}>No author matches.</li>
          ) : (
            matches.map((author) => (
              <li key={author.id} role="option" aria-selected="false">
                <button
                  type="button"
                  className={styles.suggestion}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => {
                    setOpen(false);
                    setText('');
                    onSelect(author);
                  }}
                >
                  {author.fullName}
                </button>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}
