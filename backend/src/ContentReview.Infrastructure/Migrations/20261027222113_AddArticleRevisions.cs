using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentReview.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "published_version",
                table: "articles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Articles published before revisions existed are published version 1.
            migrationBuilder.Sql("UPDATE articles SET published_version = 1 WHERE status = 'Published';");

            migrationBuilder.CreateTable(
                name: "article_published_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_published_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_published_versions_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_article_published_versions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "article_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    submission_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_revisions", x => x.id);
                    table.CheckConstraint("ck_article_revisions_status", "status IN ('Draft', 'PendingReview', 'Rejected', 'Approved')");
                    table.ForeignKey(
                        name: "fk_article_revisions_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_article_revisions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revision_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_version = table.Column<int>(type: "integer", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_reviews", x => x.id);
                    table.CheckConstraint("ck_revision_reviews_decision", "decision IN ('Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_revision_reviews_article_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "article_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_reviews_users_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_published_versions_article_id_version",
                table: "article_published_versions",
                columns: new[] { "article_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_published_versions_category_id",
                table: "article_published_versions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_revisions_article_id_number",
                table: "article_revisions",
                columns: new[] { "article_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_revisions_category_id",
                table: "article_revisions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_revisions_status_submitted_at",
                table: "article_revisions",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_article_revisions_open_per_article",
                table: "article_revisions",
                column: "article_id",
                unique: true,
                filter: "status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_revision_reviews_reviewer_id",
                table: "revision_reviews",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_revision_reviews_revision_id_submission_version",
                table: "revision_reviews",
                columns: new[] { "revision_id", "submission_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_published_versions");

            migrationBuilder.DropTable(
                name: "revision_reviews");

            migrationBuilder.DropTable(
                name: "article_revisions");

            migrationBuilder.DropColumn(
                name: "published_version",
                table: "articles");
        }
    }
}
