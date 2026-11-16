using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteConnectionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionId",
                table: "ApiRoutes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ApiRoutes");
        }
    }
}
