using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentReview.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionWithdrawnAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "withdrawn_at",
                table: "article_submissions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                table: "article_submissions");
        }
    }
}
