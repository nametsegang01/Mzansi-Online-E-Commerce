using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MzansiMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedMarketplaceCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "marketplace",
                table: "Categories",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("71111111-1111-1111-1111-111111111111"), new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Home & living", null, "home-living", new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("72222222-2222-2222-2222-222222222222"), new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Fashion", null, "fashion", new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("73333333-3333-3333-3333-333333333333"), new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Beauty", null, "beauty", new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("74444444-4444-4444-4444-444444444444"), new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Food & pantry", null, "food-pantry", new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("75555555-5555-5555-5555-555555555555"), new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Art & craft", null, "art-craft", new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("76666666-6666-6666-6666-666666666666"), new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Electronics", null, "electronics", new DateTimeOffset(new DateTime(2026, 8, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "marketplace",
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("71111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                schema: "marketplace",
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("72222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                schema: "marketplace",
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("73333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                schema: "marketplace",
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("74444444-4444-4444-4444-444444444444"));

            migrationBuilder.DeleteData(
                schema: "marketplace",
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("75555555-5555-5555-5555-555555555555"));

            migrationBuilder.DeleteData(
                schema: "marketplace",
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("76666666-6666-6666-6666-666666666666"));
        }
    }
}
