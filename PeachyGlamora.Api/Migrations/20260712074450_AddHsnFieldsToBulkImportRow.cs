using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHsnFieldsToBulkImportRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "BulkImportRows");

            migrationBuilder.AddColumn<string>(
                name: "HsnCode",
                table: "BulkImportRows",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HsnTaxRateId",
                table: "BulkImportRows",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HsnCode",
                table: "BulkImportRows");

            migrationBuilder.DropColumn(
                name: "HsnTaxRateId",
                table: "BulkImportRows");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "BulkImportRows",
                type: "decimal(12,2)",
                nullable: true);
        }
    }
}
