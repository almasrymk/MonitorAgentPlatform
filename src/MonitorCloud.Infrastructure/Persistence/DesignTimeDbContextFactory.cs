using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MonitorCloud.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only. The connection string is never used to connect when adding migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MONITOR_DESIGN_SQLSERVER")
            ?? "Server=.;Database=MonitorCloud;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.Messaging))
            .Options;
        var scope = new TenantScope();
        scope.RunAsSystem();
        return new AppDbContext(options, scope, TimeProvider.System);
    }
}
