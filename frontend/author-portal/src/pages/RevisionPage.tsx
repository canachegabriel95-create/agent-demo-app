import { useEffect, useId, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { type RevisionStatus } from '../api/articles';
import {
  getRevision,
  listRevisionReviews,
  submitRevision,
  updateRevision,
  withdrawRevision,
  type Revision,
  type RevisionReviewEntry,
} from '../api/revisions';
import {
  ConfirmDialog,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  LoadingState,
} from '../components';
import { useCategories } from '../hooks/useCategories';
import {
  BODY_MAX,
  SUMMARY_MAX,
  TITLE_MAX,
  validateDraft,
  type ArticleDraftErrors,
} from '../validation/article';
import editor from './ArticleEditorPage.module.css';
import styles from './ArticlePage.module.css';

export const REVISION_STATUS_LABELS: Record<RevisionStatus, string> = {
  Draft: 'Draft revision',
  PendingReview: 'Revision pending review',
  Rejected: 'Revision rejected',
  Approved: 'Revision approved',
};

export const REVISION_INTRO =
  'Readers keep seeing the published version until an administrator approves this revision.';
export const REVISION_PENDING_NOTICE =
  'Pending review – read-only until an administrator decides or you withdraw it.';
export const REVISION_CONFLICT_MESSAGE =
  'This revision was changed in another tab or by an administrator. Reload the latest version (your unsaved text is shown so you can copy it).';

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

/** The revision of one published article: edit it, send it for review, or take it back. Text is never rendered as HTML. */
export function RevisionPage() {
  const { id } = useParams();
  const categories = useCategories();
  const categoryFieldId = useId();

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
  const [dialog, setDialog] = useState<'submit' | 'withdraw'>();
  const [status, setStatus] = useState('');
  const [history, setHistory] = useState<RevisionReviewEntry[]>();

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

        if (error instanceof ApiError && error.status === 404) {
          setNotFound(true);
        } else {
          setLoadError(describe(error));
        }
      },
    );

    return () => controller.abort();
  }, [id, reload]);

  const revisionStatus = revision?.status;
  const submissionVersion = revision?.submissionVersion;
  useEffect(() => {
    if (!id || revisionStatus === undefined) {
      return;
    }

    const controller = new AbortController();
    listRevisionReviews(id, controller.signal).then(setHistory, () => undefined);
    return () => controller.abort();
  }, [id, revisionStatus, submissionVersion]);

  const dirty =
    revision !== undefined &&
    (title !== revision.title ||
      summary !== revision.summary ||
      body !== revision.body ||
      category !== (revision.category?.id ?? ''));

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

  function fail(failure: unknown) {
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
    if (!id || !revision || saving || conflict) {
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
      const saved = await updateRevision(
        id,
        { title, summary, body, categoryId: category },
        revision.version,
      );
      show(saved);
      setStatus('Revision saved.');
    } catch (failure) {
      fail(failure);
    } finally {
      setSaving(false);
    }
  }

  /** Saves what is on screen if it changed, then sends the revision for review. Returns a message to keep the dialog open. */
  async function confirmSubmit(): Promise<string | undefined> {
    if (!id || !revision) {
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
          id,
          { title, summary, body, categoryId: category },
          revision.version,
        );
      }

      show(await submitRevision(id, current.version));
      setStatus('Revision submitted for review.');
      setDialog(undefined);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status !== 409 && failure.status !== 400) {
        const { message, correlationId } = describe(failure);
        return correlationId ? `${message} Reference: ${correlationId}` : message;
      }

      setDialog(undefined);
      fail(failure);
    }

    return undefined;
  }

  async function confirmWithdraw(): Promise<string | undefined> {
    if (!id || !revision) {
      return undefined;
    }

    try {
      show(await withdrawRevision(id, revision.version));
      setStatus('Revision withdrawn. It is a draft again.');
      setDialog(undefined);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setDialog(undefined);
        setConflict(true);
        return undefined;
      }

      const { message, correlationId } = describe(failure);
      return correlationId ? `${message} Reference: ${correlationId}` : message;
    }

    return undefined;
  }

  function handleReload() {
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
          title="This article has no revision in progress."
          description="Open the published article and choose “Edit published article” to start one."
          action={<Link to={`/articles/${id}`}>Back to the article</Link>}
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
  const editable = can('edit');
  const locked = saving || conflict;
  const rejection = revision.status === 'Rejected' ? revision.latestReview : null;
  const localText = [title, summary, body].filter(Boolean).join('\n\n');

  return (
    <section className={styles.root} aria-labelledby="revision-heading">
      <p>
        <Link to={`/articles/${revision.articleId}`} data-testid="revision-back">
          ← Back to the published article
        </Link>
      </p>

      {conflict && (
        <div className={editor.alert} role="alert" data-testid="revision-conflict">
          <p>{REVISION_CONFLICT_MESSAGE}</p>
        </div>
      )}
      {formError && (
        <div className={editor.alert} role="alert">
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
        <h1 id="revision-heading" className={styles.title} data-testid="revision-title">
          Revision {revision.number}
        </h1>
        <span
          className={styles.badge}
          data-status={revision.status === 'Approved' ? 'Published' : revision.status}
          data-testid="revision-status-badge"
        >
          {REVISION_STATUS_LABELS[revision.status]}
        </span>
      </div>

      <p className={styles.notice} data-testid="revision-intro">
        {REVISION_INTRO}
      </p>
      {revision.status === 'PendingReview' && (
        <p className={styles.notice} data-testid="revision-readonly-notice">
          {REVISION_PENDING_NOTICE}
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
        <form className={editor.form} onSubmit={handleSave} noValidate>
          <FormField
            label="Title"
            name="title"
            value={title}
            onChange={(value) => edit(() => setTitle(value), 'title')}
            required
            disabled={locked}
            hint={`${title.trim().length} / ${TITLE_MAX} characters`}
            error={errors.title}
            testId="revision-title-input"
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
            testId="revision-summary-input"
          />
          <div className={editor.mono}>
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
              testId="revision-body-input"
            />
          </div>
          <div className={editor.field}>
            <label htmlFor={categoryFieldId} className={editor.label}>
              Category
            </label>
            <select
              id={categoryFieldId}
              name="categoryId"
              className={editor.select}
              value={category}
              onChange={(event) => edit(() => setCategory(event.target.value), 'categoryId')}
              disabled={locked || categories.status === 'loading'}
              aria-invalid={errors.categoryId ? true : undefined}
              data-testid="revision-category-input"
            >
              <option value="">— Select —</option>
              {categories.categories.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
            {errors.categoryId && <p role="alert">{errors.categoryId}</p>}
          </div>
          <div className={editor.actions}>
            <button
              type="submit"
              className={editor.primary}
              disabled={locked}
              data-testid="revision-save"
            >
              {saving ? 'Saving…' : 'Save revision'}
            </button>
            {can('submit') && (
              <button
                type="button"
                className={editor.secondary}
                disabled={locked}
                onClick={() => setDialog('submit')}
                data-testid="revision-submit"
              >
                {revision.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
              </button>
            )}
          </div>
        </form>
      ) : (
        <>
          <div className={styles.header}>
            <h2 data-testid="revision-view-title">{revision.title}</h2>
          </div>
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
          {can('withdraw') && (
            <div className={styles.actions}>
              <button
                type="button"
                className={styles.secondary}
                onClick={() => setDialog('withdraw')}
                data-testid="revision-withdraw"
              >
                Withdraw submission
              </button>
            </div>
          )}
        </>
      )}

      <div className={styles.section} data-testid="revision-history">
        <h2>Review history of revisions</h2>
        {history === undefined ? (
          <p className={styles.empty}>Loading the review history…</p>
        ) : history.length === 0 ? (
          <p className={styles.empty}>No decisions yet.</p>
        ) : (
          <ol className={styles.history}>
            {history.map((entry) => (
              <li
                key={`${entry.revisionNumber}-${entry.submissionVersion}-${entry.decidedAt}`}
                data-decision={entry.decision}
              >
                <strong>
                  Revision {entry.revisionNumber}, submission {entry.submissionVersion}:{' '}
                  {entry.decision}
                </strong>{' '}
                <span className={styles.when}>{when(entry.decidedAt)}</span>
                {entry.reason && <p className={styles.text}>{entry.reason}</p>}
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
            Submit this revision for review? It becomes read-only until an administrator decides or
            you withdraw it. The published version stays visible.
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

      {conflict && (
        <Dialog title="Revision changed elsewhere" onClose={() => setConflict(true)}>
          <p>{REVISION_CONFLICT_MESSAGE}</p>
          <label htmlFor={`${categoryFieldId}-copy`}>Your unsaved text</label>
          <textarea
            id={`${categoryFieldId}-copy`}
            className={editor.copy}
            readOnly
            value={localText}
            data-testid="revision-conflict-text"
          />
          <div className={editor.dialogActions}>
            <button
              type="button"
              className={editor.primary}
              onClick={handleReload}
              data-testid="revision-reload"
            >
              Reload latest
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}
