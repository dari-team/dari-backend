using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddBilingualStreetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // StreetAr / StreetLatin are net-new: the canonical Arabic and Latin
            // forms produced by Gemini at INSERT time so the UI can render in
            // whichever script matches the current language.
            migrationBuilder.AddColumn<string>(
                name: "StreetAr",
                table: "Addresses",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetLatin",
                table: "Addresses",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            // NormalizationVersion + StreetSearchKey were originally introduced
            // by 20260428111038_AddAiSearchFields. The corresponding properties
            // were later dropped from Address.cs — so the snapshot lost them
            // even though the DB columns remain. Re-add the columns only when
            // they're missing, so this migration runs cleanly on databases
            // that already have them and on fresh ones that don't.
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Addresses', 'NormalizationVersion') IS NULL
    ALTER TABLE dbo.Addresses ADD NormalizationVersion nvarchar(32) NULL;
");
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Addresses', 'StreetSearchKey') IS NULL
    ALTER TABLE dbo.Addresses ADD StreetSearchKey nvarchar(255) NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StreetAr",
                table: "Addresses");

            migrationBuilder.DropColumn(
                name: "StreetLatin",
                table: "Addresses");

            // Leave NormalizationVersion and StreetSearchKey alone on Down —
            // they were introduced by an earlier migration and removing them
            // here would break the FTS catalog defined in
            // 20260428144121_AddFullTextSearchOnStreet.
        }
    }
}
