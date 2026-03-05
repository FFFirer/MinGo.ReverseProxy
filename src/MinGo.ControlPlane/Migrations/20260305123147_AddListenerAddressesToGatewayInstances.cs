using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class AddListenerAddressesToGatewayInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListenerAddressesJson",
                table: "GatewayInstances",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListenerAddressesJson",
                table: "GatewayInstances");
        }
    }
}
