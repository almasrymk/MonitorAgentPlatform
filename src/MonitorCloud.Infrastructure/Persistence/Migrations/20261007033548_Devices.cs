using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorCloud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Devices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "devices");

            migrationBuilder.AddColumn<string>(
                name: "DeviceFingerprint",
                schema: "licensing",
                table: "DeviceLicenses",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Devices",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Hostname = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OsFamily = table.Column<byte>(type: "tinyint", nullable: false),
                    OsName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OsVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Architecture = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AgentVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProtocolVersion = table.Column<int>(type: "int", nullable: false),
                    LocalIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PublicIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MacAddress = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentAttempts",
                schema: "licensing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KeyPrefix = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    DeviceFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Hostname = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocationEnrollmentCodes",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CodePrefix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    Uses = table.Column<int>(type: "int", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationEnrollmentCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationEnrollmentCodes_Locations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "tenancy",
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCredentials",
                schema: "devices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RotatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    FirstFailureAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    BlockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCredentials", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_DeviceCredentials_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "devices",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceStates",
                schema: "devices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Connection = table.Column<byte>(type: "tinyint", nullable: false),
                    Health = table.Column<byte>(type: "tinyint", nullable: false),
                    LicenseState = table.Column<byte>(type: "tinyint", nullable: false),
                    UnlicensedSince = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    OsFamily = table.Column<byte>(type: "tinyint", nullable: false),
                    CpuPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RamPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    DiskPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    OpenCritical = table.Column<int>(type: "int", nullable: false),
                    OpenWarning = table.Column<int>(type: "int", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastTelemetryAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ConnectedSince = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    AppliedConfigVersion = table.Column<int>(type: "int", nullable: true),
                    LastEventSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceStates", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_DeviceStates_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "devices",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryDocuments",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Json = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryDocuments_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "devices",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_Fingerprint",
                schema: "devices",
                table: "Devices",
                columns: new[] { "TenantId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_LocationId_Status",
                schema: "devices",
                table: "Devices",
                columns: new[] { "TenantId", "LocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_Name",
                schema: "devices",
                table: "Devices",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStates_LicenseState_UnlicensedSince",
                schema: "devices",
                table: "DeviceStates",
                columns: new[] { "LicenseState", "UnlicensedSince" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStates_TenantId_Connection",
                schema: "devices",
                table: "DeviceStates",
                columns: new[] { "TenantId", "Connection" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStates_TenantId_LocationId_Health",
                schema: "devices",
                table: "DeviceStates",
                columns: new[] { "TenantId", "LocationId", "Health" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentAttempts_At",
                schema: "licensing",
                table: "EnrollmentAttempts",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentAttempts_TenantId_At",
                schema: "licensing",
                table: "EnrollmentAttempts",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDocuments_DeviceId_Kind",
                schema: "devices",
                table: "InventoryDocuments",
                columns: new[] { "DeviceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationEnrollmentCodes_CodeHash",
                schema: "tenancy",
                table: "LocationEnrollmentCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationEnrollmentCodes_LocationId",
                schema: "tenancy",
                table: "LocationEnrollmentCodes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationEnrollmentCodes_TenantId_LocationId",
                schema: "tenancy",
                table: "LocationEnrollmentCodes",
                columns: new[] { "TenantId", "LocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCredentials",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "DeviceStates",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "EnrollmentAttempts",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "InventoryDocuments",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "LocationEnrollmentCodes",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Devices",
                schema: "devices");

            migrationBuilder.DropColumn(
                name: "DeviceFingerprint",
                schema: "licensing",
                table: "DeviceLicenses");
        }
    }
}
