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
