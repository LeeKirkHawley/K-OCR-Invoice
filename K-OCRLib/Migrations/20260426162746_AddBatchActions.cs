using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BatchActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Organization = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OrgUser = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BatchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchActions_TimestampUtc",
                table: "BatchActions",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchActions");
        }
    }
}
