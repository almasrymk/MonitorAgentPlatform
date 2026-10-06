using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MonitorCloud.Application.Abstractions.Persistence;

/// <summary>Write side. Commands load aggregates through it; the unit of work saves.</summary>
public interface IAppDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
}

/// <summary>Read side: no-tracking queryables with the same tenant filters.</summary>
public interface IReadDbContext
{
    IQueryable<TEntity> Query<TEntity>() where TEntity : class;
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets pending changes after a failed command so nothing leaks into a later save.</summary>
    void DiscardChanges();
}

/// <summary>Raw SQL access for dashboards and bulk work. The tenant always comes from the tenant context.</summary>
public interface ISqlConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);

    /// <summary>Parameters <c>@TenantId</c> and <c>@Unrestricted</c> taken from the current tenant context.</summary>
    IReadOnlyDictionary<string, object?> TenantParameters();
}
