using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class OrderPickup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPickup",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PickupStoreId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PickupStoreId",
                table: "Orders",
                column: "PickupStoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Stores_PickupStoreId",
                table: "Orders",
                column: "PickupStoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Stores_PickupStoreId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PickupStoreId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsPickup",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStoreId",
                table: "Orders");
        }
    }
}
