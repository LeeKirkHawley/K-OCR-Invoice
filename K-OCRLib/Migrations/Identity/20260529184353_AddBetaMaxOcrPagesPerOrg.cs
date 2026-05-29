using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCRLib.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddBetaMaxOcrPagesPerOrg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BetaMaxOcrPages",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BetaMaxOcrPages",
                table: "Organizations");
        }
    }
}
