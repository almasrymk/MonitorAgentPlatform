using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context. Write side (<see cref="IAppDbContext"/>), read side (<see cref="IReadDbContext"/>) and
/// unit of work. Tenant isolation, outbox writing and the cross-tenant write guard live here (03 section 4).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext, TimeProvider timeProvider)
    : DbContext(options), IAppDbContext, IReadDbContext, IUnitOfWork
{
    public static readonly Error CrossTenantWrite = Error.Forbidden("AUTH_FORBIDDEN", "Cross-tenant write rejected.");

    private static readonly MethodInfo TenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo LocationFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyLocationFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Read by the query filters; EF Core evaluates them per context instance (per request).
    private bool ScopeUnrestricted => tenantContext.IsUnrestricted;
    private Guid ScopeTenantId => tenantContext.TenantId ?? Guid.Empty;
    private List<Guid> ScopeLocations => [.. tenantContext.LocationScope];

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public IQueryable<TEntity> Query<TEntity>() where TEntity : class => Set<TEntity>().AsNoTracking();

    public void DiscardChanges() => ChangeTracker.Clear();

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardTenantWrites();
        WriteDomainEventsToOutbox();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Filters are added by reflection so a new tenant-owned entity cannot be forgotten.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null).ToList())
        {
            var clr = entityType.ClrType;
            if (typeof(ILocationScoped).IsAssignableFrom(clr))
                LocationFilterMethod.MakeGenericMethod(clr).Invoke(this, [modelBuilder]);
            else if (typeof(ITenantOwned).IsAssignableFrom(clr))
                TenantFilterMethod.MakeGenericMethod(clr).Invoke(this, [modelBuilder]);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTimeOffset>().HavePrecision(3);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => ScopeUnrestricted || e.TenantId == ScopeTenantId);

    private void ApplyLocationFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ILocationScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            ScopeUnrestricted ||
            (e.TenantId == ScopeTenantId && (ScopeLocations.Count == 0 || ScopeLocations.Contains(e.LocationId))));

    /// <summary>Rejects any added, modified or deleted tenant-owned row outside the current scope.</summary>
    private void GuardTenantWrites()
    {
        if (tenantContext.IsUnrestricted)
            return;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (tenantContext.TenantId is not { } scope || entry.Entity.TenantId != scope || TenantChanged(entry))
                throw new DomainException(CrossTenantWrite);
        }
    }

    private static bool TenantChanged(EntityEntry<ITenantOwned> entry)
    {
        if (entry.State != EntityState.Modified)
            return false;
        var property = entry.Property(nameof(ITenantOwned.TenantId));
        return !Equals(property.OriginalValue, property.CurrentValue);
    }

    private void WriteDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            var tenantId = (aggregate as ITenantOwned)?.TenantId ?? tenantContext.TenantId;
            foreach (var domainEvent in aggregate.DomainEvents)
                OutboxMessages.Add(OutboxSerializer.ToMessage(domainEvent, tenantId, timeProvider));
            aggregate.ClearDomainEvents();
        }
    }
}

/// <summary>Serializes events into the outbox and back.</summary>
public static class OutboxSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static OutboxMessage ToMessage(IDomainEvent domainEvent, Guid? tenantId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var type = domainEvent.GetType();
        var occurredAt = domainEvent.OccurredAt == default ? timeProvider.GetUtcNow() : domainEvent.OccurredAt;
        return OutboxMessage.Create(domainEvent.EventId, type.FullName!, JsonSerializer.Serialize(domainEvent, type, Options), tenantId, occurredAt);
    }
}
