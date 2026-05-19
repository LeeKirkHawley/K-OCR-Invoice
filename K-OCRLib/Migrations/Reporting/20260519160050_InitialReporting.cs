using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class InitialReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OcrBatchReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BatchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrBatchReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OcrBatchReportItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OcrBatchReportId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OcrSucceeded = table.Column<bool>(type: "bit", nullable: false),
                    OcrService = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrBatchReportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrBatchReportItems_OcrBatchReports_OcrBatchReportId",
                        column: x => x.OcrBatchReportId,
                        principalTable: "OcrBatchReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OcrBatchReportItems_OcrBatchReportId",
                table: "OcrBatchReportItems",
                column: "OcrBatchReportId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrBatchReports_OrganizationId",
                table: "OcrBatchReports",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrBatchReports_OrganizationName",
                table: "OcrBatchReports",
                column: "OrganizationName");

            migrationBuilder.CreateIndex(
                name: "IX_OcrBatchReports_RecordedAtUtc",
                table: "OcrBatchReports",
                column: "RecordedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OcrBatchReportItems");

            migrationBuilder.DropTable(
                name: "OcrBatchReports");
        }
    }
}
