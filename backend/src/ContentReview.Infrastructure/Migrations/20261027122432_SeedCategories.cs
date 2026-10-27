using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ContentReview.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "id", "name", "slug", "sort_order" },
                values: new object[,]
                {
                    { new Guid("0197a000-0000-7000-8000-000000000001"), "Technology", "technology", 1 },
                    { new Guid("0197a000-0000-7000-8000-000000000002"), "Science", "science", 2 },
                    { new Guid("0197a000-0000-7000-8000-000000000003"), "Business", "business", 3 },
                    { new Guid("0197a000-0000-7000-8000-000000000004"), "Health", "health", 4 },
                    { new Guid("0197a000-0000-7000-8000-000000000005"), "Culture", "culture", 5 },
                    { new Guid("0197a000-0000-7000-8000-000000000006"), "Education", "education", 6 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("0197a000-0000-7000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("0197a000-0000-7000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("0197a000-0000-7000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("0197a000-0000-7000-8000-000000000004"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("0197a000-0000-7000-8000-000000000005"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "id",
                keyValue: new Guid("0197a000-0000-7000-8000-000000000006"));
        }
    }
}
