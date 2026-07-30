using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeachyGlamora.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLandmarkToAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Landmark",
                table: "Addresses",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Landmark",
                table: "Addresses");
        }
    }
}
