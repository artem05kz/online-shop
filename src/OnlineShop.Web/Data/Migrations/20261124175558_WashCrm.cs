using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class WashCrm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Box",
                table: "WashBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                table: "WashBookings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "WashBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManagerNote",
                table: "WashBookings",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "WashBookings",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Price",
                table: "WashBookings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "WashBookings",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "site");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Box",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "ManagerNote",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "WashBookings");
        }
    }
}
