using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class GatewayInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayInstances",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHealthy = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastHeartbeat = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CpuUsage = table.Column<double>(type: "REAL", nullable: false),
                    MemoryUsage = table.Column<double>(type: "REAL", nullable: false),
                    TotalRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorRequests = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayInstances", x => x.InstanceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayInstances_LastHeartbeat",
                table: "GatewayInstances",
                column: "LastHeartbeat");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayInstances_Status",
                table: "GatewayInstances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayInstances");
        }
    }
}
