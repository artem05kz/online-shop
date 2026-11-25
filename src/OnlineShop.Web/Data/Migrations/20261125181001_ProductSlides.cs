using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductSlides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductSlides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Script = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BgColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    ScriptColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSlides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSlides_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSlides_ProductId",
                table: "ProductSlides",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSlides_SortOrder",
                table: "ProductSlides",
                column: "SortOrder");

            // Три слайда, которые раньше были записаны в коде (HeroSlides.cs), переезжают в базу как есть.
            migrationBuilder.Sql("INSERT INTO \"ProductSlides\" (\"ProductId\", \"Script\", \"Title\", \"Text\", \"ImagePath\", \"BgColor\", \"ScriptColor\", \"SortOrder\", \"IsActive\") " +
                "SELECT \"Id\", 'Glass Gloss', 'Очиститель стёкол GLASS GLOSS', 'Готов к применению. Стёкла и зеркала без разводов, быстро высыхает.', '/img/hero/glass-gloss.webp', '#2c6bd3', '#5b98f0', 1, 1 FROM \"Products\" WHERE \"Slug\" = 'очиститель-стёкол-glass-gloss';");
            migrationBuilder.Sql("INSERT INTO \"ProductSlides\" (\"ProductId\", \"Script\", \"Title\", \"Text\", \"ImagePath\", \"BgColor\", \"ScriptColor\", \"SortOrder\", \"IsActive\") " +
                "SELECT \"Id\", 'Nano Soft', 'Автошампунь NANO SOFT', 'Для ручной мойки. Бережёт лак и хром, экономно расходуется, придаёт блеск.', '/img/hero/nano-soft.webp', '#c6ec5e', '#98c92a', 2, 1 FROM \"Products\" WHERE \"Slug\" = 'автошампунь-для-ручной-мойки-nano-soft';");
            migrationBuilder.Sql("INSERT INTO \"ProductSlides\" (\"ProductId\", \"Script\", \"Title\", \"Text\", \"ImagePath\", \"BgColor\", \"ScriptColor\", \"SortOrder\", \"IsActive\") " +
                "SELECT \"Id\", 'Top Wax', 'Холодный воск TOP WAX', 'Блеск и водоотталкивающий эффект после мойки.', '/img/hero/top-wax.webp', '#ff9447', '#ec6a18', 3, 1 FROM \"Products\" WHERE \"Slug\" = 'холодный-воск-top-wax';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductSlides");
        }
    }
}
