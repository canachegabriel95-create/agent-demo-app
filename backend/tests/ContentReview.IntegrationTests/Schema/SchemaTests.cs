using System.Text.RegularExpressions;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public partial class SchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private MigratedDatabase _database = null!;

    public async Task InitializeAsync() => _database = await MigratedDatabase.CreateAsync(postgres);

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    private static readonly string[] ExpectedTables =
    [
        "article_submissions", "articles", "audit_events", "categories",
        "invitations", "reviews", "user_sessions", "users",
    ];

    [Fact]
    public async Task All_core_tables_exist()
    {
        var tables = await QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory'");

        Assert.Equal(ExpectedTables, tables.Order().ToArray());
    }

    [Fact]
    public async Task Every_table_and_column_name_is_snake_case()
    {
        var names = await QueryAsync<string>(
            "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'");

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.Matches(SnakeCase(), n));
    }

    [Theory]
    [InlineData("users", "id", "uuid")]
    [InlineData("users", "normalized_email", "character varying")]
    [InlineData("users", "role", "text")]
    [InlineData("users", "status", "text")]
    [InlineData("users", "created_at", "timestamp with time zone")]
    [InlineData("users", "suspended_at", "timestamp with time zone")]
    [InlineData("articles", "status", "text")]
    [InlineData("articles", "submission_version", "integer")]
    [InlineData("articles", "published_at", "timestamp with time zone")]
    [InlineData("reviews", "decision", "text")]
    [InlineData("audit_events", "details", "jsonb")]
    [InlineData("audit_events", "occurred_at", "timestamp with time zone")]
    [InlineData("invitations", "expires_at", "timestamp with time zone")]
    public async Task Columns_have_the_expected_types(string table, string column, string expectedType)
    {
        var types = await QueryAsync<string>(
            $"SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}'");

        Assert.Equal(expectedType, Assert.Single(types));
    }

    [Fact]
    public async Task Required_columns_from_the_baseline_exist()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["users"] = ["id", "email", "normalized_email", "password_hash", "full_name", "bio", "role", "status", "suspension_reason", "suspended_at", "created_at", "updated_at"],
            ["user_sessions"] = ["id", "user_id", "token_hash", "created_at", "expires_at", "revoked_at", "last_seen_at"],
            ["invitations"] = ["id", "code", "email", "normalized_email", "created_by_user_id", "created_at", "expires_at", "revoked_at", "revoked_by_user_id", "used_at", "used_by_user_id"],
            ["categories"] = ["id", "name", "slug", "sort_order"],
            ["articles"] = ["id", "author_id", "category_id", "title", "summary", "body", "status", "submission_version", "submitted_at", "published_at", "created_at", "updated_at"],
            ["article_submissions"] = ["id", "article_id", "version", "title", "summary", "body", "category_id", "submitted_at"],
            ["reviews"] = ["id", "article_id", "submission_version", "reviewer_id", "decision", "reason", "decided_at"],
            ["audit_events"] = ["id", "occurred_at", "actor_user_id", "action", "entity_type", "entity_id", "details", "correlation_id"],
        };

        foreach (var (table, columns) in expected)
        {
            var actual = await QueryAsync<string>(
                $"SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}'");

            Assert.Equal(columns.Order(), actual.Order());
        }
    }

    [Theory]
    [InlineData("users", "normalized_email")]
    [InlineData("user_sessions", "token_hash")]
    [InlineData("invitations", "code")]
    [InlineData("categories", "name")]
    [InlineData("categories", "slug")]
    [InlineData("article_submissions", "article_id,version")]
    [InlineData("reviews", "article_id,submission_version")]
    public async Task Unique_indexes_exist(string table, string columns)
    {
        var index = await FindIndexAsync(table, columns);

        Assert.NotNull(index);
        Assert.True(index.IsUnique, $"{table}({columns}) should be unique");
    }

    [Fact]
    public async Task Invitation_consumption_is_unique_per_user_only_when_set()
    {
        var index = await FindIndexAsync("invitations", "used_by_user_id");

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.Equal("(used_by_user_id IS NOT NULL)", index.Predicate);
    }

    [Theory]
    [InlineData("users", "role,status")]
    [InlineData("user_sessions", "user_id")]
    [InlineData("invitations", "normalized_email")]
    [InlineData("articles", "author_id,status")]
    [InlineData("articles", "status,submitted_at")]
    [InlineData("audit_events", "action")]
    [InlineData("audit_events", "actor_user_id")]
    public async Task Supporting_indexes_exist(string table, string columns)
    {
        Assert.NotNull(await FindIndexAsync(table, columns));
    }

    [Fact]
    public async Task Audit_events_are_indexed_by_time_descending()
    {
        var definitions = await QueryAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'audit_events'");

        Assert.Contains(definitions, d => d.Contains("(occurred_at DESC)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("user_sessions", "user_id", "users", "CASCADE")]
    [InlineData("article_submissions", "article_id", "articles", "CASCADE")]
    [InlineData("articles", "author_id", "users", "RESTRICT")]
    [InlineData("articles", "category_id", "categories", "RESTRICT")]
    [InlineData("invitations", "created_by_user_id", "users", "RESTRICT")]
    [InlineData("invitations", "revoked_by_user_id", "users", "RESTRICT")]
    [InlineData("invitations", "used_by_user_id", "users", "RESTRICT")]
    [InlineData("reviews", "article_id", "articles", "RESTRICT")]
    [InlineData("reviews", "reviewer_id", "users", "RESTRICT")]
    [InlineData("audit_events", "actor_user_id", "users", "RESTRICT")]
    public async Task Foreign_keys_use_the_specified_delete_behavior(string table, string column, string referenced, string onDelete)
    {
        var rows = await QueryAsync<string>($"""
            SELECT rc.delete_rule
            FROM information_schema.referential_constraints rc
            JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = rc.constraint_name AND kcu.constraint_schema = rc.constraint_schema
            JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = rc.constraint_name AND ccu.constraint_schema = rc.constraint_schema
            WHERE kcu.table_name = '{table}' AND kcu.column_name = '{column}' AND ccu.table_name = '{referenced}'
            """);

        Assert.Equal(onDelete, Assert.Single(rows).Replace("NO ACTION", "RESTRICT"));
    }

    [Theory]
    [InlineData("users", "role", "'Administrator'", "'Author'")]
    [InlineData("users", "status", "'Active'", "'Suspended'")]
    [InlineData("articles", "status", "'Draft'", "'PendingReview'", "'Published'", "'Rejected'")]
    [InlineData("reviews", "decision", "'Approved'", "'Rejected'")]
    public async Task Enum_columns_have_check_constraints_listing_the_allowed_values(string table, string column, params string[] values)
    {
        var definitions = await QueryAsync<string>($"""
            SELECT pg_get_constraintdef(c.oid)
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            WHERE c.contype = 'c' AND t.relname = '{table}' AND pg_get_constraintdef(c.oid) LIKE '%{column}%'
            """);

        var definition = Assert.Single(definitions);
        Assert.All(values, v => Assert.Contains(v, definition));
    }

    [Fact]
    public async Task Concurrency_tokens_use_the_xmin_system_column_on_the_three_versioned_tables()
    {
        foreach (var table in new[] { "users", "invitations", "articles" })
        {
            var xmin = await QueryAsync<long>($"SELECT COUNT(*) FROM (SELECT xmin FROM {table} LIMIT 1) t");
            Assert.Single(xmin);
        }

        using var db = _database.NewContext();
        var entityTypes = db.Model.GetEntityTypes().ToDictionary(e => e.GetTableName()!, e => e);

        foreach (var table in new[] { "users", "invitations", "articles" })
        {
            var version = entityTypes[table].FindProperty("Version")!;
            Assert.True(version.IsConcurrencyToken, $"{table}.Version should be a concurrency token");
            Assert.Equal("xmin", version.GetColumnName());
        }

        foreach (var table in new[] { "user_sessions", "categories", "article_submissions", "reviews", "audit_events" })
        {
            Assert.DoesNotContain(entityTypes[table].GetProperties(), p => p.IsConcurrencyToken);
        }
    }

    [Fact]
    public async Task The_model_has_no_changes_missing_from_the_migrations()
    {
        using var db = _database.NewContext();

        Assert.False(db.Database.HasPendingModelChanges(), "Add a new EF Core migration for the model change.");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains("InitialSchema", (await db.Database.GetAppliedMigrationsAsync()).Single());
    }

    private async Task<List<T>> QueryAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var result = new List<T>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetFieldValue<T>(0));
        }

        return result;
    }

    private record IndexInfo(bool IsUnique, string? Predicate);

    /// <summary>Finds an index on exactly the given (ordered) columns.</summary>
    private async Task<IndexInfo?> FindIndexAsync(string table, string columns)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT i.indisunique, pg_get_expr(i.indpred, i.indrelid),
                   string_agg(a.attname, ',' ORDER BY k.ord)
            FROM pg_index i
            JOIN pg_class t ON t.oid = i.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            CROSS JOIN LATERAL unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE n.nspname = 'public' AND t.relname = @table
            GROUP BY i.indexrelid, i.indisunique, i.indpred, i.indrelid
            """, connection);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(2) == columns)
            {
                return new IndexInfo(reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            }
        }

        return null;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$")]
    private static partial Regex SnakeCase();
}
