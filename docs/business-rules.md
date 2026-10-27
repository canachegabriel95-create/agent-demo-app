# Business rules

Rules that the API enforces and that the portals show to users. Each rule is implemented once on the server; where a portal mirrors it for early feedback, a shared test fixture keeps both copies identical. The API always has the last word.

## Accounts

### Email addresses

| Rule | Value |
|---|---|
| Required | yes |
| Format | a bare address (`name@host.tld`) accepted by `System.Net.Mail.MailAddress`; a display name such as `Jane <jane@example.com>` is rejected; the host must contain a dot |
| Maximum length | 254 characters, after trimming |
| Normalization | `Trim()` then `ToLowerInvariant()` |

The normalized form is what is stored in `normalized_email` (unique on users, indexed on invitations) and what **every** comparison uses: login, registration, the "an account with this email already exists" check, invitation creation, and invitation redemption. So `  John.Doe@Example.COM ` and `john.doe@example.com` are the same address. The address as typed (trimmed) is kept in `email` for display.

Implementation: `EmailAddress.IsValid` and `EmailAddress.Normalize` (`ContentReview.Domain/Common/EmailAddress.cs`). Never compare emails any other way.

### Passwords

| Rule name | Requirement |
|---|---|
| `TooShort` | at least 12 characters |
| `TooLong` | at most 128 characters |
| `MissingUppercase` | at least one uppercase letter (Unicode `Lu`) |
| `MissingLowercase` | at least one lowercase letter (Unicode `Ll`) |
| `MissingDigit` | at least one decimal digit (Unicode `Nd`) |
| `MissingSymbol` | at least one symbol: any character that is not a letter, a digit or white space |

Details that matter:

- Length counts UTF-16 code units, as `string.Length` and JavaScript `String.length` do, so an emoji counts as 2.
- A space or tab is **not** a symbol, so `Str0ng Passw0rd` fails `MissingSymbol`.
- Letters of scripts without case (for example Chinese) are letters but neither upper nor lower case.
- Passwords are never trimmed, logged or returned, and are stored only as a PBKDF2 hash ([password-hashing.md](password-hashing.md)).
- The policy applies when a password is **set** (registration, the `hash-password` tool). Login does not apply it, so an existing password is never rejected for being weak; login only bounds the length.

Implementation:

| Where | What |
|---|---|
| API | `PasswordPolicy.Check` returns the violated `PasswordRule`s; `PasswordPolicy.Validate` returns the matching sentences for messages (`ContentReview.Domain/Security/PasswordPolicy.cs`) |
| `hash-password` tool | uses `PasswordPolicy.Validate` and prints which rules failed |
| Author Portal | `src/validation/passwordPolicy.ts` mirrors the rules; `PasswordRequirements` shows a live checklist ("Met." / "Not met." in words, announced politely). `POST /api/registration` enforces them (F05.2) and the registration form (F05.3) uses both |

### Keeping the two implementations identical

`docs/fixtures/password-cases.json` is a table of passwords and the rules each one breaks. It is read by the xUnit tests (`PasswordPolicyTests`) and by the Vitest tests (`passwordPolicy.test.ts`). Change a rule by changing the C# code, the TypeScript code and a fixture case together; either suite fails if one is forgotten. A test also checks that every rule appears in at least one case.

### Other account limits

| Field | Rule |
|---|---|
| Full name / display name | required, 2-100 characters, trimmed |
| Biography | optional, at most 1000 characters |

See `CLAUDE.md` for the complete list of limits.
