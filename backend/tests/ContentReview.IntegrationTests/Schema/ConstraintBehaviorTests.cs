using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.IntegrationTests.Schema;

/// <summary>Proves the constraints actually reject bad data, using the domain factories to build valid rows.</summary>
[Collection(PostgresCollection.Name)]
public class ConstraintBehaviorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";

    private MigratedDatabase _database = null!;

    public async Task InitializeAsync() => _database = await MigratedDatabase.CreateAsync(postgres);

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    private static User NewUser(string email = "jane@example.com", UserRole role = UserRole.Author) =>
        User.Create(email, "hash", "Jane Doe", role, Now);

    private static string Slug() => $"cat-{Guid.NewGuid():N}";

    private async Task<(User Author, User Admin, Category Category, Article Article)> SeedAsync()
    {
        var author = NewUser($"author-{Guid.NewGuid():N}@example.com");
        var admin = NewUser($"admin-{Guid.NewGuid():N}@example.com", UserRole.Administrator);
        var category = Category.Create($"Category {Guid.NewGuid():N}", Slug(), 1);
        var article = Article.CreateDraft(author.Id, "A title", "A summary", "A body", category.Id, Now);

        using var db = _database.NewContext();
        db.AddRange(author, admin, category, article);
        await db.SaveChangesAsync();
        return (author, admin, category, article);
    }

    private static async Task<string?> SqlStateOfAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        var postgresError = error as PostgresException
                            ?? (error.InnerException as PostgresException)
                            ?? (error.InnerException?.InnerException as PostgresException);
        Assert.True(postgresError is not null, $"Expected a PostgreSQL error but got: {error}");
        return postgresError.SqlState;
    }

    [Fact]
    public async Task A_saved_entity_round_trips_with_utc_timestamps_and_enum_names()
    {
        var (author, _, _, article) = await SeedAsync();

        using var db = _database.NewContext();
        var loaded = await db.Users.SingleAsync(u => u.Id == author.Id);
        var loadedArticle = await db.Articles.SingleAsync(a => a.Id == article.Id);

        Assert.Equal(author.NormalizedEmail, loaded.NormalizedEmail);
        Assert.Equal(UserRole.Author, loaded.Role);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
        Assert.Equal(ArticleStatus.Draft, loadedArticle.Status);

        var role = await ScalarAsync<string>($"SELECT role FROM users WHERE id = '{author.Id}'");
        Assert.Equal("Author", role);
    }

    [Fact]
    public async Task Email_uniqueness_ignores_case_and_surrounding_whitespace()
    {
        using (var db = _database.NewContext())
        {
            db.Add(NewUser("Jane@Example.com"));
            await db.SaveChangesAsync();
        }

        using var second = _database.NewContext();
        second.Add(NewUser("  jane@EXAMPLE.com "));

        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => second.SaveChangesAsync()));
    }

    [Fact]
    public async Task A_submission_can_only_be_reviewed_once()
    {
        var (_, admin, _, article) = await SeedAsync();

        using (var db = _database.NewContext())
        {
            db.Add(Review.Create(article.Id, 1, admin.Id, ReviewDecision.Approved, null, Now));
            await db.SaveChangesAsync();
        }

        using var racing = _database.NewContext();
        racing.Add(Review.Create(article.Id, 1, admin.Id, ReviewDecision.Rejected, "Rejected at the same time.", Now));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => racing.SaveChangesAsync()));

        // A later submission of the same article is a different review.
        using var next = _database.NewContext();
        next.Add(Review.Create(article.Id, 2, admin.Id, ReviewDecision.Approved, null, Now));
        await next.SaveChangesAsync();
    }

    [Fact]
    public async Task Submission_versions_are_unique_per_article()
    {
        var (_, _, category, article) = await SeedAsync();

        using (var db = _database.NewContext())
        {
            db.Add(ArticleSubmission.Create(article.Id, 1, "T", "S", "B", category.Id, Now));
            await db.SaveChangesAsync();
        }

        using var duplicate = _database.NewContext();
        duplicate.Add(ArticleSubmission.Create(article.Id, 1, "T2", "S2", "B2", category.Id, Now));

        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => duplicate.SaveChangesAsync()));
    }

    [Fact]
    public async Task Category_name_and_slug_must_be_unique()
    {
        using (var db = _database.NewContext())
        {
            db.Add(Category.Create("News", "news", 1));
            await db.SaveChangesAsync();
        }

        using var sameName = _database.NewContext();
        sameName.Add(Category.Create("News", "other-slug", 2));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => sameName.SaveChangesAsync()));

        using var sameSlug = _database.NewContext();
        sameSlug.Add(Category.Create("Other name", "news", 3));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => sameSlug.SaveChangesAsync()));
    }

    [Fact]
    public async Task Invitation_codes_are_unique_and_an_account_can_consume_only_one_invitation()
    {
        var (author, admin, _, _) = await SeedAsync();
        var first = Invitation.Create("CODE-1", "one@example.com", admin.Id, Now);
        var second = Invitation.Create("CODE-2", "two@example.com", admin.Id, Now);
        var third = Invitation.Create("CODE-3", "three@example.com", admin.Id, Now);

        using (var db = _database.NewContext())
        {
            // Several unused invitations (used_by_user_id IS NULL) are allowed.
            db.AddRange(first, second, third);
            await db.SaveChangesAsync();
        }

        using (var duplicateCode = _database.NewContext())
        {
            duplicateCode.Add(Invitation.Create("CODE-1", "four@example.com", admin.Id, Now));
            Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => duplicateCode.SaveChangesAsync()));
        }

        await ExecuteAsync($"UPDATE invitations SET used_by_user_id = '{author.Id}', used_at = now() WHERE id = '{first.Id}'");

        var state = await SqlStateOfAsync(() =>
            ExecuteAsync($"UPDATE invitations SET used_by_user_id = '{author.Id}', used_at = now() WHERE id = '{second.Id}'"));
        Assert.Equal(UniqueViolation, state);
    }

    [Theory]
    [InlineData("users", "role", "Root")]
    [InlineData("users", "status", "Banned")]
    [InlineData("articles", "status", "Archived")]
    [InlineData("reviews", "decision", "Maybe")]
    public async Task Enum_columns_reject_values_outside_the_baseline(string table, string column, string badValue)
    {
        var (author, admin, _, article) = await SeedAsync();
        var review = Review.Create(article.Id, 1, admin.Id, ReviewDecision.Approved, null, Now);
        using (var db = _database.NewContext())
        {
            db.Add(review);
            await db.SaveChangesAsync();
        }

        var id = table switch
        {
            "users" => author.Id,
            "articles" => article.Id,
            _ => review.Id,
        };

        var state = await SqlStateOfAsync(() => ExecuteAsync($"UPDATE {table} SET {column} = '{badValue}' WHERE id = '{id}'"));

        Assert.Equal(CheckViolation, state);
    }

    [Fact]
    public async Task Foreign_keys_reject_rows_that_reference_missing_parents()
    {
        using var db = _database.NewContext();
        db.Add(Article.CreateDraft(Guid.CreateVersion7(), "Orphan", "", "", null, Now));

        Assert.Equal(ForeignKeyViolation, await SqlStateOfAsync(() => db.SaveChangesAsync()));
    }

    [Fact]
    public async Task A_user_with_articles_cannot_be_deleted()
    {
        var (author, _, _, _) = await SeedAsync();

        var state = await SqlStateOfAsync(() => ExecuteAsync($"DELETE FROM users WHERE id = '{author.Id}'"));

        Assert.Equal(ForeignKeyViolation, state);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_sessions_and_deleting_an_article_removes_its_submissions()
    {
        var (author, _, category, article) = await SeedAsync();
        var loner = NewUser($"loner-{Guid.NewGuid():N}@example.com");
        var session = UserSession.Create(loner.Id, Guid.NewGuid().ToString("N"), Now, Now.AddHours(8));
        var submission = ArticleSubmission.Create(article.Id, 1, "T", "S", "B", category.Id, Now);

        using (var db = _database.NewContext())
        {
            db.AddRange(loner, session, submission);
            await db.SaveChangesAsync();
        }

        await ExecuteAsync($"DELETE FROM users WHERE id = '{loner.Id}'");
        Assert.Equal(0, await ScalarAsync<long>($"SELECT COUNT(*) FROM user_sessions WHERE id = '{session.Id}'"));

        await ExecuteAsync($"DELETE FROM articles WHERE id = '{article.Id}'");
        Assert.Equal(0, await ScalarAsync<long>($"SELECT COUNT(*) FROM article_submissions WHERE id = '{submission.Id}'"));
        Assert.Equal(1, await ScalarAsync<long>($"SELECT COUNT(*) FROM users WHERE id = '{author.Id}'"));
    }

    [Fact]
    public async Task Audit_events_store_json_details()
    {
        var audit = AuditEvent.Create("test.action", "Test", Now, detailsJson: """{"key":"value","n":3}""");

        using (var db = _database.NewContext())
        {
            db.Add(audit);
            await db.SaveChangesAsync();
        }

        var value = await ScalarAsync<string>($"SELECT details ->> 'key' FROM audit_events WHERE id = '{audit.Id}'");
        Assert.Equal("value", value);
    }

    [Fact]
    public async Task A_stale_version_is_detected_through_the_xmin_concurrency_token()
    {
        var user = NewUser();
        using (var db = _database.NewContext())
        {
            db.Add(user);
            await db.SaveChangesAsync();
            Assert.NotEqual(0u, user.Version);
        }

        using var first = _database.NewContext();
        using var second = _database.NewContext();
        var firstCopy = await first.Users.SingleAsync(u => u.Id == user.Id);
        var secondCopy = await second.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(firstCopy.Version, secondCopy.Version);

        first.Entry(firstCopy).Property(u => u.FullName).CurrentValue = "Changed by first";
        await first.SaveChangesAsync();
        Assert.NotEqual(secondCopy.Version, firstCopy.Version);

        second.Entry(secondCopy).Property(u => u.FullName).CurrentValue = "Changed by second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_article_edits_conflict_the_same_way()
    {
        var (_, _, _, article) = await SeedAsync();

        using var first = _database.NewContext();
        using var second = _database.NewContext();
        var a = await first.Articles.SingleAsync(x => x.Id == article.Id);
        var b = await second.Articles.SingleAsync(x => x.Id == article.Id);

        first.Entry(a).Property(x => x.Title).CurrentValue = "Edited once";
        await first.SaveChangesAsync();

        second.Entry(b).Property(x => x.Title).CurrentValue = "Edited twice";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
