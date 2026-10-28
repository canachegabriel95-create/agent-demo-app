# Test data and cleanup constraints

Read this when writing the **Test Data and Cleanup** section of the plan.

Facts about this system that limit setup and cleanup (verify again before relying on them):
- The only built-in route to an author account is: an administrator creates an invitation, then the author registers with that code in the Author Portal. Administrators cannot be created through the UI or API; the first one comes from `db/scripts/provision-admin.sql`.
- There is no API to delete users, invitations, published articles or audit events; only a Draft can be deleted (by its owner). Audit events are immutable. Plan for data that stays: use unique synthetic names/emails per run instead of relying on deletion.
- The `seed` / `reset` commands of `ContentReview.Tools` belong to F15 and may not exist yet. Do not check the code for them: list a full reset as a gap for `qa-explorer` to verify, unless Azure DevOps or the docs state that it is available.
- State changes such as suspension or a review decision have effects in the other portal; list them as downstream effects in the cleanup column (e.g. reactivate a suspended author, which does not restore the sessions that were revoked).
