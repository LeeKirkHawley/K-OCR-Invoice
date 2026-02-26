using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class DropIsValidationAccepted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // remove column if it exists; in SQLite this will fail if not present, so
            // you may need to adjust manually depending on your database state.
            migrationBuilder.DropColumn(
                name: "IsValidationAccepted",
                table: "Invoices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsValidationAccepted",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
