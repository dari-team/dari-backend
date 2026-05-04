using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DARI_API.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearchOnStreet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-rolled SQL — EF Core has no first-class FTS support.
            //
            // Why English (LCID 1033) on an Egyptian app:
            // StreetSearchKey is the Buckwalter-transliterated form, so it
            // ALWAYS contains ASCII letters and digits — never Arabic glyphs.
            // The English word breaker tokenizes that fine; we deliberately
            // avoid the Arabic word breaker because the input that reaches
            // FTS is no longer Arabic by Layer 1's contract.

            migrationBuilder.Sql(suppressTransaction: true, sql: @"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'dari_fts')
    EXEC('CREATE FULLTEXT CATALOG dari_fts AS DEFAULT;');
");

            migrationBuilder.Sql(suppressTransaction: true, sql: @"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes
               WHERE object_id = OBJECT_ID('dbo.Addresses'))
    EXEC('CREATE FULLTEXT INDEX ON dbo.Addresses(StreetSearchKey LANGUAGE 1033)
          KEY INDEX PK_Addresses
          ON dari_fts
          WITH CHANGE_TRACKING AUTO;');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(suppressTransaction: true, sql: @"
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Addresses'))
    EXEC('DROP FULLTEXT INDEX ON dbo.Addresses;');
");
            migrationBuilder.Sql(suppressTransaction: true, sql: @"
IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'dari_fts')
    EXEC('DROP FULLTEXT CATALOG dari_fts;');
");
        }
    }
}
