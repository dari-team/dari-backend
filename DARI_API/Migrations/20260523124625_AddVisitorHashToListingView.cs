using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitorHashToListingView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListingViews_ListingId",
                table: "ListingViews");

            migrationBuilder.AddColumn<string>(
                name: "VisitorHash",
                table: "ListingViews",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingViews_ListingId_UserId_ViewedAt",
                table: "ListingViews",
                columns: new[] { "ListingId", "UserId", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingViews_ListingId_VisitorHash_ViewedAt",
                table: "ListingViews",
                columns: new[] { "ListingId", "VisitorHash", "ViewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListingViews_ListingId_UserId_ViewedAt",
                table: "ListingViews");

            migrationBuilder.DropIndex(
                name: "IX_ListingViews_ListingId_VisitorHash_ViewedAt",
                table: "ListingViews");

            migrationBuilder.DropColumn(
                name: "VisitorHash",
                table: "ListingViews");

            migrationBuilder.CreateIndex(
                name: "IX_ListingViews_ListingId",
                table: "ListingViews",
                column: "ListingId");
        }
    }
}
