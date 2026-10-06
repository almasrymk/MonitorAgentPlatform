using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>Shared host for the generic endpoint suites of 09 section 4. They grow automatically with every new endpoint.</summary>
public abstract class EndpointSuiteBase(SqlServerFixture sql) : IAsyncLifetime
{
    protected TestApp App { get; } = new(sql);

    public virtual Task InitializeAsync() => App.InitializeAsync();

    public virtual Task DisposeAsync() => App.DisposeAsync();

    protected IReadOnlyList<ApiEndpoint> Endpoints => EndpointCatalog.All(App.Services);

    protected IReadOnlyList<ApiEndpoint> BusinessEndpoints => EndpointCatalog.Business(App.Services);

    protected static HttpRequestMessage Request(ApiEndpoint endpoint, Guid id)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Url(id));
        if (endpoint.Method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return request;
    }
}
