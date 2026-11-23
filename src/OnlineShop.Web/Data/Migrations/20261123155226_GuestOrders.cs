using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuestOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Orders",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "AccessToken",
                table: "Orders",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // У заказов, оформленных до этой миграции, своя случайная метка: иначе уникальный индекс не создать.
            migrationBuilder.Sql("UPDATE \"Orders\" SET \"AccessToken\" = lower(hex(randomblob(16))) WHERE \"AccessToken\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AccessToken",
                table: "Orders",
                column: "AccessToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_AccessToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AccessToken",
                table: "Orders");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
