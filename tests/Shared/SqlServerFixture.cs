using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace MonitorCloud.TestShared;

/// <summary>
/// One SQL Server per test run. Uses <c>MONITOR_TEST_SQLSERVER</c> when set (local instance, no Docker), otherwise a
/// Testcontainers SQL Server. Every database it creates is named <c>MonitorCloud_Test_{guid}</c> and dropped at the end.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "MONITOR_TEST_SQLSERVER";

    private readonly List<string> _databases = [];
    private readonly Lock _gate = new();
    private MsSqlContainer? _container;
    private string _serverConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var local = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(local))
        {
            _serverConnectionString = local;
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _serverConnectionString = _container.GetConnectionString();
    }

    /// <summary>A connection string for a new, empty database name (the database is created by the migrations).</summary>
    public string NewDatabase()
    {
        var name = $"MonitorCloud_Test_{Guid.NewGuid():N}";
        lock (_gate)
            _databases.Add(name);
        var builder = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = name,
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        };
        return builder.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        string[] names;
        lock (_gate)
            names = [.. _databases];

        var master = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = "master", TrustServerCertificate = true }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        foreach (var name in names)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{name}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{name}];
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
