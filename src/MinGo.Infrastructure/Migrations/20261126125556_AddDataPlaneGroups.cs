using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataPlaneGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataPlaneGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataPlaneGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstanceGroupAssignments",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceGroupAssignments", x => x.InstanceId);
                    table.ForeignKey(
                        name: "FK_InstanceGroupAssignments_DataPlaneGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "DataPlaneGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstanceGroupAssignments_GroupId",
                table: "InstanceGroupAssignments",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstanceGroupAssignments");

            migrationBuilder.DropTable(
                name: "DataPlaneGroups");
        }
    }
}
