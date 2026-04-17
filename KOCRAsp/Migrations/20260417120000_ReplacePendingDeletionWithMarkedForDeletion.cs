using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOCRAsp.Migrations
{
    public partial class ReplacePendingDeletionWithMarkedForDeletion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPendingDeletion",
                table: "Organizations");

            migrationBuilder.AddColumn<DateTime>(
                name: "MarkedForDeletionAtUtc",
                table: "Organizations",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarkedForDeletionAtUtc",
                table: "Organizations");

            migrationBuilder.AddColumn<bool>(
                name: "IsPendingDeletion",
                table: "Organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
