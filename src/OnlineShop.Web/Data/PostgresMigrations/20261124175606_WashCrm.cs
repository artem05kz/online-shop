using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.PostgresMigrations
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
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                table: "WashBookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "WashBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManagerNote",
                table: "WashBookings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "WashBookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "WashBookings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "WashBookings",
                type: "character varying(20)",
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
