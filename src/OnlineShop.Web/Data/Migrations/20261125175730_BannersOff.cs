using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Web.Data.Migrations
{
    /// <summary>
    /// 25.11.2026: главные на первом экране — слайды с товарами; баннеры из панели — заготовка на будущее.
    /// Уже загруженные баннеры остаются (картинки не удаляются), но выключаются. Включить можно во вкладке «Баннеры».
    /// </summary>
    public partial class BannersOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Banners\" SET \"IsActive\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
