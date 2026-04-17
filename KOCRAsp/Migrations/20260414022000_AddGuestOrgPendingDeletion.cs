using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOCRAsp.Migrations
{
    public partial class AddGuestOrgPendingDeletion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPendingDeletion",
                table: "Organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPendingDeletion",
                table: "Organizations");
        }
    }
}
