using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddListingKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ListingKind",
                table: "Listings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListingKind",
                table: "Listings");
        }
    }
}
