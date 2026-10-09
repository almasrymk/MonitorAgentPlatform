using System.Net.Http.Headers;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>Shared host for the generic endpoint suites of 09 section 4. They grow automatically with every new endpoint.</summary>
public abstract class EndpointSuiteBase(SqlServerFixture sql) : IAsyncLifetime
{
    protected TestApp App { get; } = new(sql);

    protected TestWorld World { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await App.InitializeAsync();
        World = await TestWorld.CreateAsync(App);
    }

    public virtual Task DisposeAsync() => App.DisposeAsync();

    protected IReadOnlyList<ApiEndpoint> Endpoints => EndpointCatalog.All(App.Services);

    protected IReadOnlyList<ApiEndpoint> BusinessEndpoints => EndpointCatalog.Business(App.Services);

    protected static HttpRequestMessage Request(ApiEndpoint endpoint, Guid id, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Url(id) + query);
        if (endpoint.Method is "POST" or "PUT" or "PATCH")
        {
            // Uploads take multipart (a small valid file), everything else a JSON body.
            var multipart = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true;
            request.Content = multipart
                ? new MultipartFormDataContent { { new ByteArrayContent("suite"u8.ToArray()), "file", "suite.txt" } }
                : new StringContent("{}", System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }
        return request;
    }
}
