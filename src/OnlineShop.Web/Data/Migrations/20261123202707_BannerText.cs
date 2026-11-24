using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BannerText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ButtonText",
                table: "Banners",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FocusX",
                table: "Banners",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<string>(
                name: "Script",
                table: "Banners",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Text",
                table: "Banners",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Banners",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "navy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ButtonText",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "FocusX",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Script",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Text",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Banners");
        }
    }
}
