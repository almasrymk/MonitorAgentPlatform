using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorCloud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Telemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "telemetry");

            migrationBuilder.CreateTable(
                name: "DiskUsageHours",
                schema: "telemetry",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Drive = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BucketUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FileSystem = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TotalGb = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UsedGb = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    FreeGb = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiskUsageHours", x => new { x.DeviceId, x.Drive, x.BucketUtc });
                });

            migrationBuilder.CreateTable(
                name: "LiveSnapshots",
                schema: "telemetry",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Json = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveSnapshots", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "MetricHours",
                schema: "telemetry",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BucketUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CpuAvg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CpuMax = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CpuP95 = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RamAvg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RamMax = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DiskActiveAvg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    DiskReadBps = table.Column<long>(type: "bigint", nullable: true),
                    DiskWriteBps = table.Column<long>(type: "bigint", nullable: true),
                    DiskResponseMs = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    NetRxBps = table.Column<long>(type: "bigint", nullable: true),
                    NetTxBps = table.Column<long>(type: "bigint", nullable: true),
                    NetRxBytes = table.Column<long>(type: "bigint", nullable: true),
                    NetTxBytes = table.Column<long>(type: "bigint", nullable: true),
                    PingMs = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    PacketLossPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    TempMaxC = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    DiskPercentMax = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Samples = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricHours", x => new { x.DeviceId, x.BucketUtc });
                });

            migrationBuilder.CreateTable(
                name: "MetricMinutes",
                schema: "telemetry",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BucketUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CpuAvg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CpuMax = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CpuP95 = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RamAvg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RamMax = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DiskActiveAvg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    DiskReadBps = table.Column<long>(type: "bigint", nullable: true),
                    DiskWriteBps = table.Column<long>(type: "bigint", nullable: true),
                    DiskResponseMs = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    NetRxBps = table.Column<long>(type: "bigint", nullable: true),
                    NetTxBps = table.Column<long>(type: "bigint", nullable: true),
                    NetRxBytes = table.Column<long>(type: "bigint", nullable: true),
                    NetTxBytes = table.Column<long>(type: "bigint", nullable: true),
                    PingMs = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    PacketLossPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    TempMaxC = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    DiskPercentMax = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Samples = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricMinutes", x => new { x.DeviceId, x.BucketUtc });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiskUsageHours_TenantId_BucketUtc",
                schema: "telemetry",
                table: "DiskUsageHours",
                columns: new[] { "TenantId", "BucketUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricHours_TenantId_BucketUtc",
                schema: "telemetry",
                table: "MetricHours",
                columns: new[] { "TenantId", "BucketUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricMinutes_TenantId_BucketUtc",
                schema: "telemetry",
                table: "MetricMinutes",
                columns: new[] { "TenantId", "BucketUtc" });

            // Page compression for the large metric tables (02 section 5).
            migrationBuilder.Sql("ALTER TABLE [telemetry].[MetricMinutes] REBUILD WITH (DATA_COMPRESSION = PAGE);");
            migrationBuilder.Sql("ALTER TABLE [telemetry].[MetricHours] REBUILD WITH (DATA_COMPRESSION = PAGE);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiskUsageHours",
                schema: "telemetry");

            migrationBuilder.DropTable(
                name: "LiveSnapshots",
                schema: "telemetry");

            migrationBuilder.DropTable(
                name: "MetricHours",
                schema: "telemetry");

            migrationBuilder.DropTable(
                name: "MetricMinutes",
                schema: "telemetry");
        }
    }
}
