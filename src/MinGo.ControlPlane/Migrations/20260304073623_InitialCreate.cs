using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityJson = table.Column<string>(type: "TEXT", nullable: false),
                    MonitoringJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clusters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LoadBalancingPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    HealthCheckJson = table.Column<string>(type: "TEXT", nullable: false),
                    GatewayConfigId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clusters_GatewayConfigs_GatewayConfigId",
                        column: x => x.GatewayConfigId,
                        principalTable: "GatewayConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ClusterId = table.Column<string>(type: "TEXT", nullable: false),
                    MatchJson = table.Column<string>(type: "TEXT", nullable: false),
                    TransformsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GatewayConfigId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routes_GatewayConfigs_GatewayConfigId",
                        column: x => x.GatewayConfigId,
                        principalTable: "GatewayConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Healthy = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClusterConfigId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Destinations_Clusters_ClusterConfigId",
                        column: x => x.ClusterConfigId,
                        principalTable: "Clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clusters_GatewayConfigId",
                table: "Clusters",
                column: "GatewayConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_ClusterConfigId",
                table: "Destinations",
                column: "ClusterConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_GatewayConfigId",
                table: "Routes",
                column: "GatewayConfigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Destinations");

            migrationBuilder.DropTable(
                name: "Routes");

            migrationBuilder.DropTable(
                name: "Clusters");

            migrationBuilder.DropTable(
                name: "GatewayConfigs");
        }
    }
}
