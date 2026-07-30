using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHsnTaxRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HsnTaxRateId",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HsnCodeSnapshot",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmountSnapshot",
                table: "OrderItems",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercentSnapshot",
                table: "OrderItems",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "HsnTaxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HsnCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HsnTaxRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_HsnTaxRateId",
                table: "Products",
                column: "HsnTaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_HsnTaxRates_HsnCode",
                table: "HsnTaxRates",
                column: "HsnCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_HsnTaxRates_HsnTaxRateId",
                table: "Products",
                column: "HsnTaxRateId",
                principalTable: "HsnTaxRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_HsnTaxRates_HsnTaxRateId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "HsnTaxRates");

            migrationBuilder.DropIndex(
                name: "IX_Products_HsnTaxRateId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HsnTaxRateId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HsnCodeSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxAmountSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxRatePercentSnapshot",
                table: "OrderItems");
        }
    }
}
