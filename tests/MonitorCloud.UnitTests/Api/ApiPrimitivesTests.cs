using MonitorCloud.Api.Infrastructure;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.Api;

public sealed class ApiPrimitivesTests
{
    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("01HZX.trace:1_2", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("line\nbreak", false)]
    public void Correlation_ids_are_echoed_only_when_safe(string value, bool valid) =>
        CorrelationIdMiddleware.IsValid(value).ShouldBe(valid);

    [Fact]
    public void Correlation_ids_longer_than_64_characters_are_replaced() =>
        CorrelationIdMiddleware.IsValid(new string('a', 65)).ShouldBeFalse();

    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.Unauthorized, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Gone, 410)]
    [InlineData(ErrorKind.PayloadTooLarge, 413)]
    [InlineData(ErrorKind.UnsupportedMediaType, 415)]
    [InlineData(ErrorKind.Locked, 423)]
    [InlineData(ErrorKind.TooManyRequests, 429)]
    public void Error_kinds_map_to_status_codes(ErrorKind kind, int status) =>
        ProblemDetailsEnricher.StatusFor(kind).ShouldBe(status);

    [Theory]
    [InlineData(401, "AUTH_UNAUTHORIZED")]
    [InlineData(403, "AUTH_FORBIDDEN")]
    [InlineData(404, "NOT_FOUND")]
    [InlineData(429, "RATE_LIMITED")]
    [InlineData(500, "INTERNAL_ERROR")]
    [InlineData(503, "INTERNAL_ERROR")]
    [InlineData(418, "ERROR")]
    public void Statuses_without_a_domain_error_get_a_default_code(int status, string code) =>
        ProblemDetailsEnricher.DefaultCode(status).ShouldBe(code);
}
