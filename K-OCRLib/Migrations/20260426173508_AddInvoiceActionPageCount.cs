using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceActionPageCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "InvoiceActions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "InvoiceActions");
        }
    }
}
