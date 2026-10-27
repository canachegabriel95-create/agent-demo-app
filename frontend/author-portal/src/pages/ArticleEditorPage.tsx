import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Navigate, useBlocker, useLocation, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  createArticle,
  getArticle,
  submitArticle,
  updateArticle,
  type Article,
  type ArticleContent,
} from '../api/articles';
import { ConfirmDialog, Dialog, ErrorState, FormField, LoadingState } from '../components';
import { useCategories } from '../hooks/useCategories';
import {
  BODY_MAX,
  SUMMARY_MAX,
  TITLE_MAX,
  validateDraft,
  type ArticleDraftErrors,
} from '../validation/article';
import styles from './ArticleEditorPage.module.css';

export const CONFLICT_DIALOG_MESSAGE =
  'This article was changed in another tab or session. Reload the latest version (your unsaved text will be shown so you can copy it).';
export const LEAVE_MESSAGE = 'You have unsaved changes. Leave this page without saving?';
export const SUBMIT_RULES =
  'Saving a draft only needs a title. To submit for review you will also need a summary (20–500 characters), a body (100–20000 characters) and a category.';

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

function contentOf(article: Article | undefined): ArticleContent {
  return {
    title: article?.title ?? '',
    summary: article?.summary ?? '',
    body: article?.body ?? '',
    categoryId: article?.category?.id ?? '',
  };
}

function savedAtLabel(date: Date) {
  return `Draft saved at ${date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`;
}

/** Creates a draft (`/articles/new`) or edits one that the lifecycle still lets the author edit. */
export function ArticleEditorPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const categories = useCategories();
  const categoryId = useId();

  const [reload, setReload] = useState(0);
  const [article, setArticle] = useState<Article>();
  const [loadError, setLoadError] = useState<Failure>();
  const [title, setTitle] = useState('');
  const [summary, setSummary] = useState('');
  const [body, setBody] = useState('');
  const [category, setCategory] = useState('');
  const [errors, setErrors] = useState<ArticleDraftErrors & { categoryId?: string }>({});
  const [formError, setFormError] = useState<Failure>();
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [confirmingSubmit, setConfirmingSubmit] = useState(false);
  const [savedMessage, setSavedMessage] = useState(
    (location.state as { savedAt?: string } | null)?.savedAt ?? '',
  );
  // Set just before the editor navigates away after a successful first save, so the guard lets it through.
  const leaving = useRef(false);

  // Load the article to edit. A new article, or the one just created here, needs no request.
  useEffect(() => {
    if (!id || article?.id === id) {
      return;
    }

    const controller = new AbortController();
    getArticle(id, controller.signal).then(
      (loaded) => {
        setArticle(loaded);
        setTitle(loaded.title);
        setSummary(loaded.summary);
        setBody(loaded.body);
        setCategory(loaded.category?.id ?? '');
        setLoadError(undefined);
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describe(error));
        }
      },
    );

    return () => controller.abort();
    // `article` is read only to skip the request for an article this page just created.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, reload]);

  const saved = contentOf(article);
  const dirty =
    title !== saved.title ||
    summary !== saved.summary ||
    body !== saved.body ||
    category !== saved.categoryId;

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
    setSavedMessage('');
    if (field) {
      setErrors((previous) => ({ ...previous, [field]: undefined }));
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving || conflict || (id && !article)) {
      return;
    }

    const found = validateDraft(title, summary, body);
    setErrors(found);
    setFormError(undefined);
    setSavedMessage('');
    if (found.title || found.summary || found.body) {
      return;
    }

    const content = { title, summary, body, categoryId: category };
    setSaving(true);
    try {
      const result = article
        ? await updateArticle(article.id, content, article.version)
        : await createArticle(content);
      setArticle(result);
      setTitle(result.title);
      setSummary(result.summary);
      setBody(result.body);
      setCategory(result.category?.id ?? '');
      const message = savedAtLabel(new Date());
      setSavedMessage(message);
      if (!article) {
        leaving.current = true;
        navigate(`/articles/${result.id}/edit`, { replace: true, state: { savedAt: message } });
      }
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setConflict(true);
      } else if (failure instanceof ApiError && failure.status === 400) {
        if (!showFieldErrors(failure)) {
          setFormError(describe(failure));
        }
      } else {
        setFormError(describe(failure));
      }
    } finally {
      setSaving(false);
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

  /** Saves what is on screen if it changed, then sends the article for review. Returns a message to keep the dialog open. */
  async function confirmSubmit(): Promise<string | undefined> {
    if (!article) {
      return undefined;
    }

    setFormError(undefined);
    setSavedMessage('');
    try {
      let current = article;
      if (dirty) {
        const found = validateDraft(title, summary, body);
        setErrors(found);
        if (found.title || found.summary || found.body) {
          setConfirmingSubmit(false);
          return undefined;
        }

        current = await updateArticle(
          article.id,
          { title, summary, body, categoryId: category },
          article.version,
        );
        setArticle(current);
      }

      await submitArticle(current.id, current.version);
      leaving.current = true;
      navigate(`/articles/${current.id}`, { replace: true });
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setConfirmingSubmit(false);
        setConflict(true);
      } else if (failure instanceof ApiError && failure.status === 400) {
        setConfirmingSubmit(false);
        if (!showFieldErrors(failure)) {
          setFormError(describe(failure));
        }
      } else {
        const { message, correlationId } = describe(failure);
        return correlationId ? `${message} Reference: ${correlationId}` : message;
      }
    }

    return undefined;
  }

  function handleReload() {
    setConflict(false);
    setSavedMessage('');
    setErrors({});
    setFormError(undefined);
    setArticle(undefined);
    setReload((value) => value + 1);
  }

  const heading = id ? 'Edit article' : 'New article';

  if (loadError) {
    return (
      <section className={styles.root} aria-labelledby="article-editor-heading">
        <h1 id="article-editor-heading">{heading}</h1>
        <ErrorState
          title="The article could not be loaded"
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

  if (id && !article) {
    return (
      <section className={styles.root} aria-labelledby="article-editor-heading">
        <h1 id="article-editor-heading">{heading}</h1>
        <LoadingState message="Loading the article…" />
      </section>
    );
  }

  // Only drafts and rejected articles can be edited; anything else goes to the article's page.
  if (article && !article.allowedActions.includes('edit')) {
    return <Navigate to={`/articles/${article.id}`} replace />;
  }

  const locked = saving || conflict;
  const localText = [title, summary, body].filter(Boolean).join('\n\n');

  return (
    <section className={styles.root} aria-labelledby="article-editor-heading">
      <h1 id="article-editor-heading">{heading}</h1>
      <p className={styles.rules} data-testid="article-rules">
        {SUBMIT_RULES}
      </p>

      {conflict && (
        <div className={styles.alert} role="alert" data-testid="article-conflict">
          <p>{CONFLICT_DIALOG_MESSAGE}</p>
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

      <form className={styles.form} onSubmit={handleSubmit} noValidate>
        <FormField
          label="Title"
          name="title"
          value={title}
          onChange={(value) => edit(() => setTitle(value), 'title')}
          required
          disabled={locked}
          hint={`${title.trim().length} / ${TITLE_MAX} characters`}
          error={errors.title}
          testId="article-title"
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
          testId="article-summary"
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
            testId="article-body"
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
            data-testid="article-category"
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
            data-testid="article-save"
          >
            {saving ? 'Saving…' : 'Save draft'}
          </button>
          {article?.allowedActions.includes('submit') && (
            <button
              type="button"
              className={styles.secondary}
              disabled={locked}
              onClick={() => setConfirmingSubmit(true)}
              data-testid="article-submit"
            >
              {article.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
            </button>
          )}
          <div role="status" data-testid="article-status">
            {savedMessage && !dirty && <p className={styles.success}>{savedMessage}</p>}
          </div>
        </div>
      </form>

      {confirmingSubmit && article && (
        <ConfirmDialog
          title={article.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
          confirmLabel={article.status === 'Rejected' ? 'Resubmit' : 'Submit'}
          busyLabel="Submitting…"
          confirmTestId="article-submit-confirm"
          onClose={() => setConfirmingSubmit(false)}
          onConfirm={confirmSubmit}
        >
          <p>
            Submit “{title.trim() || article.title}” for review? It becomes read-only until an
            administrator decides or you withdraw it.
            {dirty && ' Your unsaved changes are saved first.'}
          </p>
        </ConfirmDialog>
      )}

      {conflict && (
        <Dialog title="Article changed elsewhere" onClose={() => setConflict(true)}>
          <p>{CONFLICT_DIALOG_MESSAGE}</p>
          <label htmlFor={`${categoryId}-copy`}>Your unsaved text</label>
          <textarea
            id={`${categoryId}-copy`}
            className={styles.copy}
            readOnly
            value={localText}
            data-testid="article-conflict-text"
          />
          <div className={styles.dialogActions}>
            <button
              type="button"
              className={styles.primary}
              onClick={handleReload}
              data-testid="article-reload"
            >
              Reload latest
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}
