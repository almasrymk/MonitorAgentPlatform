using System.Data.Common;
using Microsoft.Data.SqlClient;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Persistence;

namespace MonitorCloud.Infrastructure.Persistence;

internal sealed class SqlConnectionFactory(DatabaseOptionsAccessor options, ITenantContext tenantContext) : ISqlConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public IReadOnlyDictionary<string, object?> TenantParameters() =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["TenantId"] = tenantContext.TenantId,
            ["Unrestricted"] = tenantContext.IsUnrestricted,
        };
}

/// <summary>The database connection string for code that writes outside EF Core (telemetry bulk copy).</summary>
public sealed record DatabaseOptionsAccessor(string ConnectionString);
