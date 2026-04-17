using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOCRAsp.Migrations
{
    public partial class AddGuestOrganizationMarker : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGuestOrganization",
                table: "Organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGuestOrganization",
                table: "Organizations");
        }
    }
}
