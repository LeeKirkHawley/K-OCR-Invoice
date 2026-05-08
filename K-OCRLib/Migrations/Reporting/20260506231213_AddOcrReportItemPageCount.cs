using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class AddOcrReportItemPageCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: add PageCount only if the column does not already exist.
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[OcrBatchReportItems]') AND name = 'PageCount'
)
BEGIN
    ALTER TABLE [OcrBatchReportItems]
        ADD [PageCount] int NOT NULL DEFAULT 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "OcrBatchReportItems");
        }
    }
}
