using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkImportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulkImportJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalRecords = table.Column<int>(type: "int", nullable: false),
                    ValidRecords = table.Column<int>(type: "int", nullable: false),
                    InvalidRecords = table.Column<int>(type: "int", nullable: false),
                    DuplicateRecords = table.Column<int>(type: "int", nullable: false),
                    WarningRecords = table.Column<int>(type: "int", nullable: false),
                    ImportedRecords = table.Column<int>(type: "int", nullable: false),
                    FailedRecords = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulkImportJobs_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BulkImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    Errors = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Warnings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Occasion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Material = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BasePrice = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    CompareAtPrice = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsNewArrival = table.Column<bool>(type: "bit", nullable: false),
                    IsBestSeller = table.Column<bool>(type: "bit", nullable: false),
                    IsTrending = table.Column<bool>(type: "bit", nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ImportStatus = table.Column<int>(type: "int", nullable: false),
                    ImportError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedProductId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulkImportRows_BulkImportJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "BulkImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulkImportJobs_UploadedById",
                table: "BulkImportJobs",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_BulkImportRows_JobId",
                table: "BulkImportRows",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkImportRows");

            migrationBuilder.DropTable(
                name: "BulkImportJobs");
        }
    }
}
