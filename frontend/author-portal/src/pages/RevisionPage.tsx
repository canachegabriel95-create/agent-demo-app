import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useBlocker, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  REVISION_STATUS_LABELS,
  deleteRevision,
  getRevision,
  submitRevision,
  updateRevision,
  withdrawRevision,
  type Revision,
} from '../api/revisions';
import type { ArticleContent } from '../api/articles';
import { ConfirmDialog, EmptyState, ErrorState, FormField, LoadingState } from '../components';
import { useCategories } from '../hooks/useCategories';
import {
  BODY_MAX,
  SUMMARY_MAX,
  TITLE_MAX,
  validateDraft,
  type ArticleDraftErrors,
} from '../validation/article';
import styles from './RevisionPage.module.css';

export const LEAVE_MESSAGE = 'You have unsaved changes. Leave this page without saving?';
export const PUBLISHED_STAYS_NOTICE =
  'Readers keep seeing the published version until an administrator approves this revision.';
export const PENDING_NOTICE =
  'Pending review – read-only until an administrator decides or you withdraw it.';
export const APPROVED_NOTICE = 'Approved – this revision is now the published version.';
export const CONFLICT_MESSAGE =
  'This revision changed in another tab or session, or an administrator decided. Refresh to see its current state.';
export const SUBMIT_RULES =
  'Saving a revision only needs a title. To submit it for review you will also need a summary (20–500 characters), a body (100–20000 characters) and a category.';

const dateTime = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' });

interface Failure {
  message: string;
  correlationId?: string;
}

function describe(error: unknown): Failure {
  if (error instanceof ApiError) {
    return { message: error.detail ?? error.title, correlationId: error.correlationId };
  }

  return { message: 'Something went wrong. Please try again.' };
}

function when(value: string | null) {
  return value ? dateTime.format(new Date(value)) : undefined;
}

function contentOf(revision: Revision | undefined): ArticleContent {
  return {
    title: revision?.title ?? '',
    summary: revision?.summary ?? '',
    body: revision?.body ?? '',
    categoryId: revision?.category?.id ?? '',
  };
}

/**
 * A revision of one of the author's published articles. A Draft or Rejected revision is edited here; a pending or
 * approved one is shown read-only. The published article itself never changes until the revision is approved.
 */
export function RevisionPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const categories = useCategories();
  const categoryId = useId();

  const [reload, setReload] = useState(0);
  const [revision, setRevision] = useState<Revision>();
  const [notFound, setNotFound] = useState(false);
  const [loadError, setLoadError] = useState<Failure>();
  const [title, setTitle] = useState('');
  const [summary, setSummary] = useState('');
  const [body, setBody] = useState('');
  const [category, setCategory] = useState('');
  const [errors, setErrors] = useState<ArticleDraftErrors & { categoryId?: string }>({});
  const [formError, setFormError] = useState<Failure>();
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState('');
  const [dialog, setDialog] = useState<'submit' | 'withdraw' | 'discard'>();
  const leaving = useRef(false);

  function show(loaded: Revision) {
    setRevision(loaded);
    setTitle(loaded.title);
    setSummary(loaded.summary);
    setBody(loaded.body);
    setCategory(loaded.category?.id ?? '');
  }

  useEffect(() => {
    if (!id) {
      return;
    }

    const controller = new AbortController();
    getRevision(id, controller.signal).then(
      (loaded) => {
        show(loaded);
        setNotFound(false);
        setLoadError(undefined);
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        // Somebody else's revision looks exactly like one that does not exist.
        if (error instanceof ApiError && error.status === 404) {
          setNotFound(true);
        } else {
          setLoadError(describe(error));
        }
      },
    );

    return () => controller.abort();
  }, [id, reload]);

  const editable = revision?.allowedActions.includes('edit') ?? false;
  const saved = contentOf(revision);
  const dirty =
    editable &&
    (title !== saved.title ||
      summary !== saved.summary ||
      body !== saved.body ||
      category !== saved.categoryId);

  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      dirty && !leaving.current && currentLocation.pathname !== nextLocation.pathname,
  );
  useEffect(() => {
    if (blocker.state !== 'blocked') {
      return;
    }

    if (window.confirm(LEAVE_MESSAGE)) {
      blocker.proceed();
    } else {
      blocker.reset();
    }
  }, [blocker]);

  useEffect(() => {
    if (!dirty) {
      return;
    }

    function warn(event: BeforeUnloadEvent) {
      event.preventDefault();
    }

    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  function edit(change: () => void, field?: keyof ArticleDraftErrors | 'categoryId') {
    change();
    setStatus('');
    if (field) {
      setErrors((previous) => ({ ...previous, [field]: undefined }));
    }
  }

  /** Puts the API's field messages (draft or submission rules) on the fields. Returns whether any field was named. */
  function showFieldErrors(failure: ApiError) {
    const server = {
      title: failure.errors.title?.[0],
      summary: failure.errors.summary?.[0],
      body: failure.errors.body?.[0],
      categoryId: failure.errors.categoryId?.[0],
    };
    setErrors(server);
    return Boolean(server.title || server.summary || server.body || server.categoryId);
  }

  function handleFailure(failure: unknown) {
    if (failure instanceof ApiError && failure.status === 409) {
      setConflict(true);
    } else if (failure instanceof ApiError && failure.status === 400) {
      if (!showFieldErrors(failure)) {
        setFormError(describe(failure));
      }
    } else {
      setFormError(describe(failure));
    }
  }

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving || conflict || !revision) {
      return;
    }

    const found = validateDraft(title, summary, body);
    setErrors(found);
    setFormError(undefined);
    setStatus('');
    if (found.title || found.summary || found.body) {
      return;
    }

    setSaving(true);
    try {
      const result = await updateRevision(
        revision.id,
        { title, summary, body, categoryId: category },
        revision.version,
      );
      show(result);
      setStatus(
        `Revision saved at ${new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`,
      );
    } catch (failure) {
      handleFailure(failure);
    } finally {
      setSaving(false);
    }
  }

  /** Saves what is on screen if it changed, then sends the revision for review. Returns a message to keep the dialog open. */
  async function confirmSubmit(): Promise<string | undefined> {
    if (!revision) {
      return undefined;
    }

    setFormError(undefined);
    setStatus('');
    try {
      let current = revision;
      if (dirty) {
        const found = validateDraft(title, summary, body);
        setErrors(found);
        if (found.title || found.summary || found.body) {
          setDialog(undefined);
          return undefined;
        }

        current = await updateRevision(
          revision.id,
          { title, summary, body, categoryId: category },
          revision.version,
        );
      }

      const submitted = await submitRevision(current.id, current.version);
      show(submitted);
      setErrors({});
      setStatus('Submitted for review. The published version stays visible until it is approved.');
      setDialog(undefined);
    } catch (failure) {
      if (failure instanceof ApiError && (failure.status === 409 || failure.status === 400)) {
        setDialog(undefined);
        handleFailure(failure);
      } else {
        const { message, correlationId } = describe(failure);
        return correlationId ? `${message} Reference: ${correlationId}` : message;
      }
    }

    return undefined;
  }

  async function confirmWithdraw(): Promise<string | undefined> {
    if (!revision) {
      return undefined;
    }

    try {
      const withdrawn = await withdrawRevision(revision.id, revision.version);
      show(withdrawn);
      setStatus('Revision withdrawn. It is a draft again.');
      setDialog(undefined);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setDialog(undefined);
        setConflict(true);
      } else {
        const { message, correlationId } = describe(failure);
        return correlationId ? `${message} Reference: ${correlationId}` : message;
      }
    }

    return undefined;
  }

  async function confirmDiscard(): Promise<string | undefined> {
    if (!revision) {
      return undefined;
    }

    try {
      await deleteRevision(revision.id, revision.version);
      leaving.current = true;
      navigate(`/articles/${revision.articleId}`, { replace: true });
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setDialog(undefined);
        setConflict(true);
      } else {
        const { message, correlationId } = describe(failure);
        return correlationId ? `${message} Reference: ${correlationId}` : message;
      }
    }

    return undefined;
  }

  function refresh() {
    setConflict(false);
    setStatus('');
    setErrors({});
    setFormError(undefined);
    setRevision(undefined);
    setReload((value) => value + 1);
  }

  if (notFound) {
    return (
      <section className={styles.root} aria-labelledby="revision-heading">
        <h1 id="revision-heading">Revision not found</h1>
        <EmptyState
          title="We could not find that revision."
          description="It may have been discarded, or it may not be yours."
          action={<Link to="/articles">Go to My Articles</Link>}
        />
      </section>
    );
  }

  if (loadError) {
    return (
      <section className={styles.root} aria-labelledby="revision-heading">
        <h1 id="revision-heading">Revision</h1>
        <ErrorState
          title="The revision could not be loaded"
          message={loadError.message}
          correlationId={loadError.correlationId}
          onRetry={() => {
            setLoadError(undefined);
            setReload((value) => value + 1);
          }}
        />
      </section>
    );
  }

  if (!revision) {
    return (
      <section className={styles.root} aria-labelledby="revision-heading">
        <h1 id="revision-heading">Revision</h1>
        <LoadingState message="Loading the revision…" />
      </section>
    );
  }

  const can = (action: string) => revision.allowedActions.includes(action);
  const locked = saving || conflict;
  const rejection = revision.status === 'Rejected' ? revision.latestReview : null;

  return (
    <section className={styles.root} aria-labelledby="revision-heading">
      {conflict && (
        <div className={styles.alert} role="alert" data-testid="revision-conflict">
          <p>{CONFLICT_MESSAGE}</p>
          <button
            type="button"
            className={styles.secondary}
            onClick={refresh}
            data-testid="revision-refresh"
          >
            Refresh
          </button>
        </div>
      )}
      {formError && (
        <div className={styles.alert} role="alert">
          <p>
            {formError.message}
            {formError.correlationId && (
              <>
                {' '}
                Reference: <code>{formError.correlationId}</code>
              </>
            )}
          </p>
        </div>
      )}
      <div role="status" data-testid="revision-flash">
        {status && !dirty && <p className={styles.success}>{status}</p>}
      </div>

      <div className={styles.header}>
        <h1 id="revision-heading" className={styles.title}>
          Revision of “{revision.title}”
        </h1>
        <span
          className={styles.badge}
          data-status={revision.status}
          data-testid="revision-status-badge"
        >
          {REVISION_STATUS_LABELS[revision.status]}
        </span>
      </div>

      <p className={styles.notice} data-testid="revision-published-notice">
        {PUBLISHED_STAYS_NOTICE}{' '}
        <Link to={`/articles/${revision.articleId}`} data-testid="revision-published-link">
          View the published article
        </Link>
      </p>
      {revision.status === 'PendingReview' && (
        <p className={styles.notice} data-testid="revision-readonly-notice">
          {PENDING_NOTICE}
        </p>
      )}
      {revision.status === 'Approved' && (
        <p className={styles.notice} data-testid="revision-readonly-notice">
          {APPROVED_NOTICE}
        </p>
      )}

      {rejection && (
        <div
          className={styles.review}
          role="region"
          aria-label="Rejection"
          data-testid="revision-review"
        >
          <strong>Rejected on {when(rejection.decidedAt)}</strong>
          {rejection.reason && <p data-testid="revision-review-reason">{rejection.reason}</p>}
        </div>
      )}

      {editable ? (
        <>
          <p className={styles.rules} data-testid="revision-rules">
            {SUBMIT_RULES}
          </p>
          <form className={styles.form} onSubmit={handleSave} noValidate>
            <FormField
              label="Title"
              name="title"
              value={title}
              onChange={(value) => edit(() => setTitle(value), 'title')}
              required
              disabled={locked}
              hint={`${title.trim().length} / ${TITLE_MAX} characters`}
              error={errors.title}
              testId="revision-title"
            />
            <FormField
              label="Summary"
              name="summary"
              multiline
              rows={3}
              value={summary}
              onChange={(value) => edit(() => setSummary(value), 'summary')}
              disabled={locked}
              hint={`${summary.trim().length} / ${SUMMARY_MAX} characters`}
              error={errors.summary}
              testId="revision-summary"
            />
            <div className={styles.mono}>
              <FormField
                label="Body"
                name="body"
                multiline
                rows={14}
                value={body}
                onChange={(value) => edit(() => setBody(value), 'body')}
                disabled={locked}
                hint={`${body.trim().length} / ${BODY_MAX} characters`}
                error={errors.body}
                testId="revision-body"
              />
            </div>
            <div className={styles.field}>
              <label htmlFor={categoryId} className={styles.label}>
                Category
              </label>
              <select
                id={categoryId}
                name="categoryId"
                className={styles.select}
                value={category}
                onChange={(event) => edit(() => setCategory(event.target.value), 'categoryId')}
                disabled={locked || categories.status === 'loading'}
                aria-invalid={errors.categoryId ? true : undefined}
                data-testid="revision-category"
              >
                <option value="">— Select —</option>
                {categories.categories.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
              {categories.status === 'error' && (
                <p role="alert">
                  The categories could not be loaded.{' '}
                  <button type="button" className={styles.secondary} onClick={categories.reload}>
                    Try again
                  </button>
                </p>
              )}
              {errors.categoryId && <p role="alert">{errors.categoryId}</p>}
            </div>
            <div className={styles.actions}>
              <button
                type="submit"
                className={styles.primary}
                disabled={locked}
                data-testid="revision-save"
              >
                {saving ? 'Saving…' : 'Save revision'}
              </button>
              {can('submit') && (
                <button
                  type="button"
                  className={styles.secondary}
                  disabled={locked}
                  onClick={() => setDialog('submit')}
                  data-testid="revision-submit"
                >
                  {revision.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
                </button>
              )}
              {can('delete') && (
                <button
                  type="button"
                  className={styles.secondary}
                  disabled={locked}
                  onClick={() => setDialog('discard')}
                  data-testid="revision-discard"
                >
                  Discard revision
                </button>
              )}
            </div>
          </form>
        </>
      ) : (
        <>
          <dl className={styles.facts}>
            <dt>Category</dt>
            <dd data-testid="revision-view-category">{revision.category?.name ?? 'No category'}</dd>
            <dt>Last updated</dt>
            <dd>{when(revision.updatedAt)}</dd>
            {revision.submittedAt && (
              <>
                <dt>Submitted</dt>
                <dd>{when(revision.submittedAt)}</dd>
              </>
            )}
          </dl>
          <div className={styles.actions}>
            {can('withdraw') && (
              <button
                type="button"
                className={styles.secondary}
                disabled={locked}
                onClick={() => setDialog('withdraw')}
                data-testid="revision-withdraw"
              >
                Withdraw submission
              </button>
            )}
          </div>
          <div className={styles.section}>
            <h2>Title</h2>
            <p className={styles.text} data-testid="revision-view-title">
              {revision.title}
            </p>
          </div>
          <div className={styles.section}>
            <h2>Summary</h2>
            <p className={styles.text} data-testid="revision-view-summary">
              {revision.summary}
            </p>
          </div>
          <div className={styles.section}>
            <h2>Body</h2>
            <p className={styles.text} data-testid="revision-view-body">
              {revision.body}
            </p>
          </div>
        </>
      )}

      <div className={styles.section} data-testid="revision-history">
        <h2>Review history</h2>
        {revision.reviews.length === 0 ? (
          <p className={styles.empty}>No decisions yet.</p>
        ) : (
          <ol className={styles.history}>
            {revision.reviews.map((review) => (
              <li
                key={`${review.submissionVersion}-${review.decidedAt}`}
                data-decision={review.decision}
              >
                <strong>
                  Submission {review.submissionVersion}: {review.decision}
                </strong>{' '}
                <span className={styles.when}>{when(review.decidedAt)}</span>
                {review.reason && <p className={styles.text}>{review.reason}</p>}
              </li>
            ))}
          </ol>
        )}
      </div>

      {dialog === 'submit' && (
        <ConfirmDialog
          title={revision.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
          confirmLabel={revision.status === 'Rejected' ? 'Resubmit' : 'Submit'}
          busyLabel="Submitting…"
          confirmTestId="revision-submit-confirm"
          onClose={() => setDialog(undefined)}
          onConfirm={confirmSubmit}
        >
          <p>
            Submit this revision of “{revision.title}” for review? It becomes read-only until an
            administrator decides or you withdraw it. The published version stays visible.
            {dirty && ' Your unsaved changes are saved first.'}
          </p>
        </ConfirmDialog>
      )}
      {dialog === 'withdraw' && (
        <ConfirmDialog
          title="Withdraw submission"
          confirmLabel="Withdraw"
          busyLabel="Withdrawing…"
          confirmTestId="revision-withdraw-confirm"
          onClose={() => setDialog(undefined)}
          onConfirm={confirmWithdraw}
        >
          <p>
            Withdraw this revision from review? It becomes an editable draft again and you can
            submit it later.
          </p>
        </ConfirmDialog>
      )}
      {dialog === 'discard' && (
        <ConfirmDialog
          title="Discard revision"
          confirmLabel="Discard"
          busyLabel="Discarding…"
          confirmTestId="revision-discard-confirm"
          variant="danger"
          onClose={() => setDialog(undefined)}
          onConfirm={confirmDiscard}
        >
          <p>Discard this revision? The published article stays as it is. This cannot be undone.</p>
        </ConfirmDialog>
      )}
    </section>
  );
}
