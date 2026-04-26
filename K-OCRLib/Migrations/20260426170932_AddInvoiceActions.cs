using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BatchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InvoiceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OrgUser = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceActions_TimestampUtc",
                table: "InvoiceActions",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceActions");
        }
    }
}
