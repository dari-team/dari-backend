using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddListingTitleDescriptionTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "Listings",
                type: "nvarchar(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "Listings",
                type: "nvarchar(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleAr",
                table: "Listings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleEn",
                table: "Listings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "TitleAr",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "TitleEn",
                table: "Listings");
        }
    }
}
