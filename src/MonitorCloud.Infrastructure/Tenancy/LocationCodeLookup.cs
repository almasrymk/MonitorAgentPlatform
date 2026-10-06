using Dapper;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Tenancy;

internal sealed class LocationCodeLookup(ISqlConnectionFactory connections) : ILocationCodeLookup
{
    public async Task<bool> IsTakenAsync(string code, Guid? exceptLocationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var parameters = new DynamicParameters(connections.TenantParameters());
        parameters.Add("Code", code);
        parameters.Add("ExceptId", exceptLocationId);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(SqlResources.Get("Tenancy.LocationCodeTaken"), parameters, cancellationToken: cancellationToken));
    }
}
