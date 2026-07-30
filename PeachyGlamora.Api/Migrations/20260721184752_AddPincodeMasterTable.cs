using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPincodeMasterTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PincodePosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Pincode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    OfficeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    District = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PincodePosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PincodePosts_Pincode",
                table: "PincodePosts",
                column: "Pincode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PincodePosts");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Name", "Slug" },
                values: new object[] { "Pendants", "pendants" });
        }
    }
}
