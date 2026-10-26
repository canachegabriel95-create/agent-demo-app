import styles from './Pagination.module.css';

export interface PaginationProps {
  /** 1-based current page. */
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}

export function Pagination({ page, pageSize, totalCount, onPageChange }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)));
  const current = Math.min(Math.max(1, page), totalPages);

  return (
    <nav className={styles.root} aria-label="Pagination">
      <button
        type="button"
        className={styles.button}
        onClick={() => onPageChange(current - 1)}
        disabled={current <= 1}
      >
        Previous page
      </button>
      <span className={styles.status} aria-live="polite">
        Page {current} of {totalPages}
      </span>
      <button
        type="button"
        className={styles.button}
        onClick={() => onPageChange(current + 1)}
        disabled={current >= totalPages}
      >
        Next page
      </button>
    </nav>
  );
}
