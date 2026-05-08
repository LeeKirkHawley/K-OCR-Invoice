using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class AddOcrBatchReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: tables may already exist if a previously-deleted migration
            // ran against this database. Guards prevent duplicate-object errors.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[OcrBatchReports]', 'U') IS NULL
BEGIN
    CREATE TABLE [OcrBatchReports] (
        [Id]               int           NOT NULL IDENTITY(1,1),
        [OrganizationName] nvarchar(200) NOT NULL,
        [BatchName]        nvarchar(200) NOT NULL,
        [RecordedAtUtc]    datetime2     NOT NULL,
        CONSTRAINT [PK_OcrBatchReports] PRIMARY KEY ([Id])
    );
END");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[OcrBatchReportItems]', 'U') IS NULL
BEGIN
    CREATE TABLE [OcrBatchReportItems] (
        [Id]               int           NOT NULL IDENTITY(1,1),
        [OcrBatchReportId] int           NOT NULL,
        [FileName]         nvarchar(500) NOT NULL,
        [OcrSucceeded]     bit           NOT NULL,
        [OcrService]       nvarchar(100) NOT NULL,
        CONSTRAINT [PK_OcrBatchReportItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OcrBatchReportItems_OcrBatchReports_OcrBatchReportId]
            FOREIGN KEY ([OcrBatchReportId]) REFERENCES [OcrBatchReports] ([Id])
            ON DELETE CASCADE
    );
END");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_OcrBatchReportItems_OcrBatchReportId'
                 AND object_id = OBJECT_ID(N'[OcrBatchReportItems]'))
    CREATE INDEX [IX_OcrBatchReportItems_OcrBatchReportId]
        ON [OcrBatchReportItems] ([OcrBatchReportId]);");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_OcrBatchReports_OrganizationName'
                 AND object_id = OBJECT_ID(N'[OcrBatchReports]'))
    CREATE INDEX [IX_OcrBatchReports_OrganizationName]
        ON [OcrBatchReports] ([OrganizationName]);");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_OcrBatchReports_RecordedAtUtc'
                 AND object_id = OBJECT_ID(N'[OcrBatchReports]'))
    CREATE INDEX [IX_OcrBatchReports_RecordedAtUtc]
        ON [OcrBatchReports] ([RecordedAtUtc]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OcrBatchReportItems");
            migrationBuilder.DropTable(name: "OcrBatchReports");
        }
    }
}
