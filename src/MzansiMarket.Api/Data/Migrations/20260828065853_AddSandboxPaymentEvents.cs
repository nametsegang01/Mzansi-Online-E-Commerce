using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MzansiMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxPaymentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_OrderId",
                schema: "marketplace",
                table: "PaymentRecords");

            migrationBuilder.AddColumn<string>(
                name: "PaymentKey",
                schema: "marketplace",
                table: "PaymentRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentProviderEvents",
                schema: "marketplace",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentProviderEvents_PaymentRecords_PaymentRecordId",
                        column: x => x.PaymentRecordId,
                        principalSchema: "marketplace",
                        principalTable: "PaymentRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_OrderId_PaymentKey",
                schema: "marketplace",
                table: "PaymentRecords",
                columns: new[] { "OrderId", "PaymentKey" },
                unique: true,
                filter: "\"PaymentKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_PaymentRecordId",
                schema: "marketplace",
                table: "PaymentProviderEvents",
                column: "PaymentRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_Provider_EventId",
                schema: "marketplace",
                table: "PaymentProviderEvents",
                columns: new[] { "Provider", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentProviderEvents",
                schema: "marketplace");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_OrderId_PaymentKey",
                schema: "marketplace",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PaymentKey",
                schema: "marketplace",
                table: "PaymentRecords");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_OrderId",
                schema: "marketplace",
                table: "PaymentRecords",
                column: "OrderId");
        }
    }
}
