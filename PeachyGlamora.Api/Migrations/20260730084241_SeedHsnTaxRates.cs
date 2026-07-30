using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedHsnTaxRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "HsnTaxRates",
                columns: new[] { "Id", "CreatedAt", "Description", "HsnCode", "IsActive", "TaxRatePercent" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 7, 12, 8, 19, 41, 0, DateTimeKind.Utc), "Imitation jewellery of base metal, whether or not plated", "711711", true, 3.00m },
                    { 2, new DateTime(2026, 7, 12, 8, 19, 41, 0, DateTimeKind.Utc), "Other imitation jewellery", "711790", true, 3.00m },
                    { 3, new DateTime(2026, 7, 12, 8, 19, 41, 0, DateTimeKind.Utc), "Fashion accessories of plastics", "391926", true, 12.00m },
                    { 4, new DateTime(2026, 7, 12, 8, 19, 41, 0, DateTimeKind.Utc), "Imitation jewellery of base metal, gold/silver plated", "711719", true, 3.00m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "HsnTaxRates",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "HsnTaxRates",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "HsnTaxRates",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "HsnTaxRates",
                keyColumn: "Id",
                keyValue: 4);
        }
    }
}
