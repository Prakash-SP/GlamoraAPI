using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantTaggingAndOrderSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductVariantId",
                table: "WishlistItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorSnapshot",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeSnapshot",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_ProductVariantId",
                table: "WishlistItems",
                column: "ProductVariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_WishlistItems_ProductVariants_ProductVariantId",
                table: "WishlistItems",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WishlistItems_ProductVariants_ProductVariantId",
                table: "WishlistItems");

            migrationBuilder.DropIndex(
                name: "IX_WishlistItems_ProductVariantId",
                table: "WishlistItems");

            migrationBuilder.DropColumn(
                name: "ProductVariantId",
                table: "WishlistItems");

            migrationBuilder.DropColumn(
                name: "ColorSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SizeSnapshot",
                table: "OrderItems");
        }
    }
}
