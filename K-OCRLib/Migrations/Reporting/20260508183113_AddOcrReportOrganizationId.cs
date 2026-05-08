using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class AddOcrReportOrganizationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[OcrBatchReports]') AND name = 'OrganizationId'
)
BEGIN
    ALTER TABLE [OcrBatchReports]
        ADD [OrganizationId] nvarchar(450) NOT NULL DEFAULT '';
END");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_OcrBatchReports_OrganizationId'
                 AND object_id = OBJECT_ID(N'[OcrBatchReports]'))
    CREATE INDEX [IX_OcrBatchReports_OrganizationId]
        ON [OcrBatchReports] ([OrganizationId]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OcrBatchReports_OrganizationId",
                table: "OcrBatchReports");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "OcrBatchReports");
        }
    }
}
