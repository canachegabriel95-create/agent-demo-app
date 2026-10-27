# Article editor (Author Portal)

The editor is where an author writes and saves drafts. It uses the article API in [api.md](api.md#articles-author) and the rules in [article-lifecycle.md](article-lifecycle.md).

## Routes

| Route | What it does |
|---|---|
| `/articles/new` | An empty form. The first successful save creates the draft and replaces the URL with `/articles/:id/edit` (history is replaced, so Back does not return to the empty form). |
| `/articles/:id/edit` | Loads the article. Only reachable when `allowedActions` contains `edit` (Draft, Rejected); otherwise the author is redirected to `/articles/:id`. A missing or someone else's article shows the "could not be loaded" error state. |

`/articles/:id` is a placeholder until the article page arrives with F11.

## Form

| Field | `data-testid` | Limit (counter) |
|---|---|---|
| Title | `article-title` | required, at most 150 |
| Summary | `article-summary` | at most 500 |
| Body (plain text, monospace) | `article-body` | at most 20000 |
| Category | `article-category` | optional; "— Select —" = none; list from `useCategories` |
| Save draft | `article-save` | |

Counters count the trimmed text, like the API. A hint explains that a summary (20–500), a body (100–20000) and a category are needed to **submit**; submitting itself arrives with F09.3. A missing or too long field is refused in the browser with the API's sentence; field errors from the API (400) are shown on the field.

After a save the status area reads "Draft saved at HH:mm" until the next edit.

## Saving and conflicts

- Every save sends the `version` last read; the response's new version is used for the next save.
- A 409 (the article was changed elsewhere, or can no longer be edited) opens a dialog with the sentence "This article was changed in another tab or session…", a read-only copy of the unsaved title, summary and body for copying, and **Reload latest** (`article-reload`). The form behind keeps the typed text but cannot be saved. Reloading fetches the latest version, replaces the form and re-checks `allowedActions` (an article that is now in review redirects away).

## Unsaved changes

Leaving the page with unsaved changes (link, Back, Log out) asks for confirmation; closing the tab shows the browser's own warning. A successful first save does not ask when the editor moves to the edit route.

Tests: `frontend/author-portal/src/pages/ArticleEditorPage.test.tsx`.

# Article view and draft deletion (F08.4)

`/articles/:id` shows one of the author's own articles: title, status badge (Draft, Pending review, Published, Rejected; always written out), category, created/updated/submitted/published dates, summary and body. A rejected article also shows the latest rejection reason.

## Safe rendering

Everything an author typed (title, summary, body, rejection reason, category name) is rendered as a React text node, in elements with `white-space: pre-wrap` and `overflow-wrap: anywhere`. There is no `dangerouslySetInnerHTML` and no markdown-to-HTML. `react/no-danger` is an ESLint **error** in both portals (`eslint.config.js`). `ArticlePage.test.tsx` renders `<script>alert(1)</script>` and `<img src=x onerror=alert(1)>` and asserts the text is literal and that no `script`, `img` or `b` element exists.

## Actions

The action bar is driven by `allowedActions`: **Edit** (a link to the editor) when `edit` is allowed, **Delete** when `delete` is allowed. Lifecycle buttons (submit, withdraw) arrive with F09.3.

## Deleting a draft

Delete opens a confirmation, "Delete draft “title”? This cannot be undone.", with **Cancel** focused first and **Delete** (`article-delete-confirm`). Cancel and Escape keep the draft. A successful delete sends the loaded `version`, then goes to My Articles with the message "Draft “title” deleted." (My Articles itself arrives with F11; until then the placeholder page shows the message). A 409 (changed elsewhere, or no longer a draft) closes the dialog, reloads the article and shows an alert; other failures show inside the dialog.

## Not found

A missing article and somebody else's article both show "Article not found" with a link to My Articles (the API answers 404 for both).

Tests: `frontend/author-portal/src/pages/ArticlePage.test.tsx`.

# Lifecycle actions and rejection feedback (F09.3)

## Buttons

The action bar on `/articles/:id` follows `allowedActions` (the API's state machine, see [article-lifecycle.md](article-lifecycle.md)):

| Status | Buttons | Notice |
|---|---|---|
| Draft | Edit, **Submit for review** (`article-submit`), Delete | |
| PendingReview | **Withdraw submission** (`article-withdraw`) | "Pending review – read-only until an administrator decides or you withdraw it." |
| Rejected | Edit, **Resubmit for review** | Rejection panel (below) |
| Published | none | "Published – this article is read-only." |

Submit and withdraw always ask first (`ConfirmDialog`: Cancel is focused; the confirm buttons are `article-submit-confirm` and `article-withdraw-confirm`). The submit dialog says the article becomes read-only until an administrator decides or the author withdraws it. After success the page shows the new status and a confirmation line.

The editor also has the submit button (not for an article that was never saved). If the form has unsaved changes the dialog says they are saved first; the editor then saves (checking the draft rules locally) and submits the version that save returned, and goes to `/articles/:id`.

## Errors

- **400 on submit** (submission rules are stricter than draft rules): on the article page a panel lists every broken rule with a **Fix in editor** link; in the editor the messages appear on the fields (`title`, `summary`, `body`, `categoryId`). Nothing changed.
- **409** (the status or version changed, e.g. an administrator decided first): "This article changed status. Refresh to continue." with a **Refresh** button (`article-refresh`) that reloads the article and its actions. In the editor the existing conflict dialog opens.
- Other failures stay inside the dialog with the correlation id.

## Rejection feedback and history

A Rejected article shows a panel above the content: "Rejected on <date>" and the reason, as plain text. **Review history** lists every decision from `GET /api/articles/{id}/reviews` (newest submission first: "Submission 2: Rejected", date, reason). The reviewer's identity is never sent to authors. A load failure shows "The review history could not be loaded" with Try again.

Tests: `ArticlePage.test.tsx`, `ArticleEditorPage.test.tsx`; backend `ArticleReviewsTests.cs`.
