using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortainerInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortainerInstances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CacheSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    HostAddress = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DataPlaneService = table.Column<string>(type: "TEXT", nullable: false),
                    SelfStackNamesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SelfImagePatternsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraExcludedStacksJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortainerInstances", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortainerInstances");
        }
    }
}
