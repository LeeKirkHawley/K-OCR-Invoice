using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations
{
    /// <inheritdoc />
    public partial class RenameShippingToDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Shipping",
                table: "Invoices",
                newName: "Discount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Discount",
                table: "Invoices",
                newName: "Shipping");
        }
    }
}
