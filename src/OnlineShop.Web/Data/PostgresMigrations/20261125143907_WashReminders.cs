using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class WashReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClientReminderSent",
                table: "WashBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WaitReminderSent",
                table: "WashBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Старые записи уже обработаны вручную: не напоминать о них пачкой при первом запуске.
            migrationBuilder.Sql("UPDATE \"WashBookings\" SET \"WaitReminderSent\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientReminderSent",
                table: "WashBookings");

            migrationBuilder.DropColumn(
                name: "WaitReminderSent",
                table: "WashBookings");
        }
    }
}
