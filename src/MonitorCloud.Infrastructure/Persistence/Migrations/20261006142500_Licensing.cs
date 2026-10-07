using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorCloud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Licensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "licensing");

            migrationBuilder.CreateTable(
                name: "DeviceLicenses",
                schema: "licensing",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UnlicensedSince = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    Token = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    Kid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CheckAfter = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    OfflineValidUntil = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceLicenses", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "SyncState",
                schema: "licensing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cursor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastFullReconcileAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantEntitlements",
                schema: "licensing",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PlanName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubscriptionStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MaxDevices = table.Column<int>(type: "int", nullable: true),
                    ActiveSeats = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    RenewsAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    SyncError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Features = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseIds = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEntitlements", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceLicenses_CheckAfter",
                schema: "licensing",
                table: "DeviceLicenses",
                column: "CheckAfter");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceLicenses_TenantId_State",
                schema: "licensing",
                table: "DeviceLicenses",
                columns: new[] { "TenantId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlements_PlanCode_SubscriptionStatus",
                schema: "licensing",
                table: "TenantEntitlements",
                columns: new[] { "PlanCode", "SubscriptionStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlements_RenewsAt",
                schema: "licensing",
                table: "TenantEntitlements",
                column: "RenewsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceLicenses",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "SyncState",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "TenantEntitlements",
                schema: "licensing");
        }
    }
}
