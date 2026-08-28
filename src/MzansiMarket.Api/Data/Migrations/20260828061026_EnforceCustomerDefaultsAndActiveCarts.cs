using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MzansiMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCustomerDefaultsAndActiveCarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Carts_CustomerId",
                schema: "marketplace",
                table: "Carts",
                column: "CustomerId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_UserId",
                schema: "marketplace",
                table: "Addresses",
                column: "UserId",
                unique: true,
                filter: "\"IsDefault\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carts_CustomerId",
                schema: "marketplace",
                table: "Carts");

            migrationBuilder.DropIndex(
                name: "IX_Addresses_UserId",
                schema: "marketplace",
                table: "Addresses");
        }
    }
}
