using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryAndCurrencyToInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Invoices",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorCountry",
                table: "Invoices",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "VendorCountry",
                table: "Invoices");
        }
    }
}
