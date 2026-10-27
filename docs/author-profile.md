# Author profile (Author Portal)

Authors see their account details and keep their name and biography up to date. The HTTP details are in [api.md](api.md#my-profile-author).

## Profile page

Route `/profile` (navigation item "Profile"), signed-in authors only like the rest of the portal.

| Part | Behavior |
|---|---|
| Account | Email, Role and "Registered on" (long date) as plain text. There is no control for them: they cannot be changed here. |
| Full name | Required, 2-100 characters after trimming. Text fields keep the author's own text; nothing is interpreted as markup. |
| Biography | Optional, at most 1000 characters. Emptying it clears it. |
| Counters | A live "n / 100 characters (at least 2)" and "n / 1000 characters" under the fields. |
| Validation | The API's rules and sentences (`src/validation/profile.ts`), checked on Save. Nothing is sent while a field is invalid. A field's message disappears as soon as that field is edited. A 400 from the API is shown next to its field. |
| Save | Sends the trimmed values and the `version` that was loaded, then shows "Profile saved" in a `role="status"` region (announced politely) and keeps showing exactly what the API returned. Editing again removes the message. |
| Header | The name in the header changes right after a save (`useAuth().updateUser`), without reloading the page or asking `/api/auth/me` again. |
| Conflict (409) | Someone (for example an administrator) changed the profile since it was loaded. The form is disabled and says "This profile was changed by someone else. Reload to continue." with a **Reload** button that loads the current profile and discards what was typed. |
| Other failures | Shown with the reference id; what was typed stays. |
| States | Loading, and an error with "Try again" if the profile cannot be loaded. |

### Unsaved changes

While the fields differ from the saved profile:

- Leaving the page through a link, the back button or Log out asks "You have unsaved changes. Leave this page without saving?" (`useBlocker`). Cancel keeps the page and the typed text; OK leaves.
- Closing or reloading the tab triggers the browser's own warning (`beforeunload`).
- Changing the text and changing it back, or saving, means there is nothing to warn about.

Test ids: `profile-fullname`, `profile-bio`, `profile-save`, `profile-status`, `profile-email`, `profile-role`, `profile-registered`, `profile-conflict`, `profile-reload`.

## Where the code is

| What | Where |
|---|---|
| API client | `frontend/author-portal/src/api/profile.ts` |
| Page and styles | `src/pages/ProfilePage.tsx`, `ProfilePage.module.css` |
| Validation (mirrors the API) | `src/validation/profile.ts` |
| Header update | `updateUser` in `src/auth/AuthContext.ts` and `AuthProvider.tsx` |

## Tests

`ProfilePage.test.tsx` and `profile.test.ts` (Vitest): read-only fields, counters, validation limits, request body and version, success message, header update, 409 and failure handling, the unsaved-changes prompt in each direction. The Playwright scenario (edit, refresh, log out and in, values persist) is added by F16.4.
