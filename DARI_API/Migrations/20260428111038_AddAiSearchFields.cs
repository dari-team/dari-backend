using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSearchFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DownPayment",
                table: "Listings",
                type: "decimal(15,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinishingLevel",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentType",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizationVersion",
                table: "Addresses",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetSearchKey",
                table: "Addresses",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownPayment",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "FinishingLevel",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "PaymentType",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "NormalizationVersion",
                table: "Addresses");

            migrationBuilder.DropColumn(
                name: "StreetSearchKey",
                table: "Addresses");
        }
    }
}
