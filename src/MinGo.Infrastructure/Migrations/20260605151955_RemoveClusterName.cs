using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveClusterName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "ApiClusters");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ApiClusters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
