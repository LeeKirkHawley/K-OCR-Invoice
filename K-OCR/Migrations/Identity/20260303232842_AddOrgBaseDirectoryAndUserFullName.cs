using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K_OCR.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddOrgBaseDirectoryAndUserFullName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseDirectory",
                table: "Organizations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseDirectory",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "AspNetUsers");
        }
    }
}
