using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudflareSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudflareSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ApiToken = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudflareSettings");
        }
    }
}
