using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MonitorCloud.TestShared;

public static class HttpAssertions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("code").GetString().ShouldBe(code, body);
        return json;
    }

    public static async Task<T> ShouldBeOkAsync<T>(this HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Json);
}
