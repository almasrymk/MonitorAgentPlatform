using Microsoft.EntityFrameworkCore;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.ArchitectureTests;

/// <summary>Rules 4 (query filters) and 10 (no pending model changes) of 09 section 3. No database connection is opened.</summary>
public sealed class PersistenceConventionTests : IDisposable
{
    private readonly AppDbContext _db;

    public PersistenceConventionTests()
    {
        var scope = new TenantScope();
        scope.RunAsSystem();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=.;Database=MonitorCloud_Model;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.Messaging))
            .Options;
        _db = new AppDbContext(options, scope, TimeProvider.System);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Every_tenant_owned_entity_has_a_query_filter()
    {
        var offenders = _db.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .Where(t => t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_table_lives_in_a_module_schema()
    {
        var offenders = _db.Model.GetEntityTypes()
            .Where(t => t.GetTableName() is not null && !Schemas.All.Contains(t.GetSchema() ?? "dbo"))
            .Select(t => t.ClrType.Name)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Ids_are_never_generated_by_the_database()
    {
        var offenders = _db.Model.GetEntityTypes()
            .SelectMany(t => t.FindPrimaryKey()?.Properties ?? [])
            .Where(p => p.ClrType == typeof(Guid) && p.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_model_has_no_pending_migration_changes() =>
        _db.Database.HasPendingModelChanges().ShouldBeFalse("Run 'dotnet ef migrations add'.");
}
