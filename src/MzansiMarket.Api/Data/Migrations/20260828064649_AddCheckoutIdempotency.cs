using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MzansiMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutKey",
                schema: "marketplace",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionCode",
                schema: "marketplace",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_CheckoutKey",
                schema: "marketplace",
                table: "Orders",
                columns: new[] { "CustomerId", "CheckoutKey" },
                unique: true,
                filter: "\"CheckoutKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_CheckoutKey",
                schema: "marketplace",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutKey",
                schema: "marketplace",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PromotionCode",
                schema: "marketplace",
                table: "Orders");
        }
    }
}
