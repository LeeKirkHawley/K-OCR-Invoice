using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class UpdateInvoiceTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsValidationAccepted",
                table: "Invoices",
                newName: "IsInvoiceAccepted");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceEdits",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "ValidatedOcrText",
                table: "Invoices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsInvoiceAccepted",
                table: "Invoices",
                newName: "IsValidationAccepted");

            migrationBuilder.DropColumn(
                name: "InvoiceEdits",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "ValidatedOcrText",
                table: "Invoices",
                type: "TEXT",
                nullable: true);
        }
    }
}
