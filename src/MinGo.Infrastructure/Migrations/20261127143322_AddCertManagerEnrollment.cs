using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCertManagerEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CertificateEnrollments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    IsWildcard = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseStaging = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExternalCertificateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastThumbprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateEnrollments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CertManagerSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertManagerSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateEnrollments_Domain",
                table: "CertificateEnrollments",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateEnrollments");

            migrationBuilder.DropTable(
                name: "CertManagerSettings");
        }
    }
}
