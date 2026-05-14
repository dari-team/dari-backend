using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCompletionRefAndComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "ListingReferenceSeq",
                startValue: 100000000L);

            migrationBuilder.AddColumn<int>(
                name: "CompletionStatus",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Listings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceNumber",
                table: "Listings",
                type: "int",
                nullable: false,
                defaultValueSql: "NEXT VALUE FOR ListingReferenceSeq");

            migrationBuilder.CreateTable(
                name: "Complaints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Complaints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Complaints_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Complaints_Users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_ReferenceNumber",
                table: "Listings",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ListingId",
                table: "Complaints",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ReporterId",
                table: "Complaints",
                column: "ReporterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Complaints");

            migrationBuilder.DropIndex(
                name: "IX_Listings_ReferenceNumber",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "CompletionStatus",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "Listings");

            migrationBuilder.DropSequence(
                name: "ListingReferenceSeq");
        }
    }
}
