using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MzansiMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionDomainConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_Value",
                schema: "marketplace",
                table: "Promotions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_MinimumOrderAmount",
                schema: "marketplace",
                table: "Promotions",
                sql: "\"MinimumOrderAmount\" IS NULL OR \"MinimumOrderAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_UsageLimit",
                schema: "marketplace",
                table: "Promotions",
                sql: "\"UsageLimit\" IS NULL OR \"UsageLimit\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_Value",
                schema: "marketplace",
                table: "Promotions",
                sql: "\"Value\" >= 0 AND ((\"Type\" = 'Percentage' AND \"Value\" <= 100) OR (\"Type\" = 'FixedAmount' AND \"Value\" > 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_MinimumOrderAmount",
                schema: "marketplace",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_UsageLimit",
                schema: "marketplace",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_Value",
                schema: "marketplace",
                table: "Promotions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_Value",
                schema: "marketplace",
                table: "Promotions",
                sql: "\"Value\" > 0");
        }
    }
}
