using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiLifestyleAndCloudinaryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Listings",
                type: "nvarchar(MAX)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)");

            migrationBuilder.AddColumn<string>(
                name: "AiGeneratedDescription",
                table: "Listings",
                type: "nvarchar(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiStandardizedFinishing",
                table: "Listings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Listings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifestyleScoreBreakdown",
                table: "Listings",
                type: "nvarchar(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LifestyleScoreCalculatedAt",
                table: "Listings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Bytes",
                table: "Images",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "Images",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Images",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Images",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiGeneratedDescription",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "AiStandardizedFinishing",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "LifestyleScoreBreakdown",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "LifestyleScoreCalculatedAt",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "Bytes",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Images");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Listings",
                type: "nvarchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(MAX)");
        }
    }
}
