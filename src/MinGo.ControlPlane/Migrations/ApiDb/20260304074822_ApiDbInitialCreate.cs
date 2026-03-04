using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.ControlPlane.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class ApiDbInitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiCertificates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DomainName = table.Column<string>(type: "TEXT", nullable: false),
                    CertificateType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", nullable: true),
                    Issuer = table.Column<string>(type: "TEXT", nullable: true),
                    Thumbprint = table.Column<string>(type: "TEXT", nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    CertificateData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCertificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiClusters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LoadBalancingPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    HealthCheckJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiClusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiRoutes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ClusterId = table.Column<string>(type: "TEXT", nullable: false),
                    MatchJson = table.Column<string>(type: "TEXT", nullable: false),
                    TransformsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiDestinations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Healthy = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClusterId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiDestinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiDestinations_ApiClusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "ApiClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiDestinations_ClusterId",
                table: "ApiDestinations",
                column: "ClusterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiCertificates");

            migrationBuilder.DropTable(
                name: "ApiDestinations");

            migrationBuilder.DropTable(
                name: "ApiRoutes");

            migrationBuilder.DropTable(
                name: "ApiClusters");
        }
    }
}
