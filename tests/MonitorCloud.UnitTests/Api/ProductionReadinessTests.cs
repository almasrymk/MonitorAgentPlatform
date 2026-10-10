using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MonitorCloud.Api;
using NSubstitute;

namespace MonitorCloud.UnitTests.Api;

public sealed class ProductionReadinessTests
{
    private static Dictionary<string, string?> Ready() => new()
    {
        ["ConnectionStrings:Monitor"] = "Server=db;Database=MonitorCloud",
        ["Jwt:SigningKey"] = new string('u', 48),
        ["Jwt:DeviceSigningKey"] = new string('d', 48),
        ["Seed:DemoData"] = "false",
        ["Licensing:Mode"] = "Live",
        ["Licensing:ClientSecret"] = "from-the-environment",
        ["Cors:Origins:0"] = "https://portal.example.com",
        ["Agent:PublicBaseUrl"] = "https://api.example.com",
        ["Agent:GatewayUrl"] = "https://gateway.example.com",
        ["Portal:BaseUrl"] = "https://portal.example.com",
        ["Storage:Root"] = "/var/lib/monitor-cloud",
        ["AllowedHosts"] = "api.example.com;gateway.example.com",
        ["Serilog:MinimumLevel:Default"] = "Information",
        ["Commands:SigningKey"] = "-----BEGIN PRIVATE KEY-----\nfrom-the-environment\n-----END PRIVATE KEY-----",
    };

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Env(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    [Fact]
    public void A_complete_production_configuration_has_no_problems() =>
        ProductionReadiness.Problems(Config(Ready())).ShouldBeEmpty();

    [Theory]
    [InlineData("ConnectionStrings:Monitor", "", "ConnectionStrings:Monitor")]
    [InlineData("Jwt:SigningKey", "short", "Jwt:SigningKey must have at least 32")]
    [InlineData("Jwt:SigningKey", "DEV-ONLY-user-signing-key-0123456789abcdef", "Jwt:SigningKey is a development")]
    [InlineData("Jwt:DeviceSigningKey", "TEST-ONLY-device-signing-key-0123456789abcdef", "Jwt:DeviceSigningKey is a development")]
    [InlineData("Seed:DemoData", "true", "Seed:DemoData")]
    [InlineData("Licensing:Mode", "Fake", "Licensing:Mode")]
    [InlineData("Licensing:ClientSecret", "CI-ONLY-secret", "Licensing:ClientSecret")]
    [InlineData("Cors:Origins:0", "http://localhost:4300", "Cors:Origins contains")]
    [InlineData("Agent:PublicBaseUrl", "http://api.example.com", "Agent:PublicBaseUrl")]
    [InlineData("Agent:GatewayUrl", "https://localhost:5301", "Agent:GatewayUrl")]
    [InlineData("Portal:BaseUrl", "https://127.0.0.1", "Portal:BaseUrl")]
    [InlineData("Storage:Root", "", "Storage:Root")]
    [InlineData("AllowedHosts", "*", "AllowedHosts")]
    [InlineData("Serilog:MinimumLevel:Default", "Debug", "Serilog:MinimumLevel:Default")]
    [InlineData("Commands:SigningKey", "", "Commands:SigningKey")]
    public void Each_unsafe_setting_is_named(string key, string value, string expected)
    {
        var values = Ready();
        values[key] = value;

        ProductionReadiness.Problems(Config(values)).ShouldHaveSingleItem().ShouldContain(expected);
    }

    [Fact]
    public void Equal_signing_keys_and_missing_origins_are_problems()
    {
        var values = Ready();
        values["Jwt:DeviceSigningKey"] = values["Jwt:SigningKey"];
        values.Remove("Cors:Origins:0");

        ProductionReadiness.Problems(Config(values)).ShouldBe(["Jwt:SigningKey and Jwt:DeviceSigningKey must differ.", "Cors:Origins is empty."], ignoreOrder: true);
    }

    [Fact]
    public void Only_production_refuses_to_start_and_the_message_never_contains_a_secret()
    {
        var values = Ready();
        values["Jwt:SigningKey"] = "DEV-ONLY-user-signing-key-0123456789abcdef";
        var config = Config(values);

        Should.NotThrow(() => ProductionReadiness.EnsureReady(config, Env(Environments.Development)));
        var error = Should.Throw<InvalidOperationException>(() => ProductionReadiness.EnsureReady(config, Env(Environments.Production)));
        error.Message.ShouldContain("Jwt:SigningKey");
        error.Message.ShouldNotContain("DEV-ONLY-user-signing-key");
    }
}
