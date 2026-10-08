using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorCloud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Configuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "config");

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                schema: "monitoring",
                table: "MonitorPoints",
                type: "nvarchar(max)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceConfigurationAcks",
                schema: "config",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedVersion = table.Column<int>(type: "int", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RejectedVersion = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConfigurationAcks", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "DeviceConfigurations",
                schema: "config",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DocumentJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConfigurations", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "TenantConfigurationDefaults",
                schema: "config",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantConfigurationDefaults", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceConfigurationAcks_TenantId",
                schema: "config",
                table: "DeviceConfigurationAcks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceConfigurations_TenantId",
                schema: "config",
                table: "DeviceConfigurations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceConfigurationAcks",
                schema: "config");

            migrationBuilder.DropTable(
                name: "DeviceConfigurations",
                schema: "config");

            migrationBuilder.DropTable(
                name: "TenantConfigurationDefaults",
                schema: "config");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                schema: "monitoring",
                table: "MonitorPoints");
        }
    }
}
