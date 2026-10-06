using NetArchTest.Rules;

namespace MonitorCloud.ArchitectureTests;

/// <summary>Dependency rules 1-5 of 01 section 2.</summary>
public sealed class LayerTests
{
    private static void ShouldPass(TestResult result) =>
        result.IsSuccessful.ShouldBeTrue($"Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void SharedKernel_references_nothing_of_ours() =>
        ShouldPass(Types.InAssembly(Assemblies.SharedKernel).ShouldNot()
            .HaveDependencyOnAny("MonitorCloud.Domain", "MonitorCloud.Application", "MonitorCloud.Infrastructure", "MonitorCloud.Api", "MonitorCloud.AgentGateway", "MonitorCloud.AgentProtocol")
            .GetResult());

    [Fact]
    public void Domain_references_only_the_shared_kernel() =>
        ShouldPass(Types.InAssembly(Assemblies.Domain).ShouldNot()
            .HaveDependencyOnAny(
                "MonitorCloud.Application", "MonitorCloud.Infrastructure", "MonitorCloud.Api", "MonitorCloud.AgentGateway", "MonitorCloud.AgentProtocol",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MediatR", "System.Net.Http", "FluentValidation")
            .GetResult());

    [Fact]
    public void Domain_assembly_references_only_the_shared_kernel()
    {
        var ours = Assemblies.Domain.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("MonitorCloud.", StringComparison.Ordinal));

        ours.ShouldBe(["MonitorCloud.SharedKernel"]);
    }

    [Fact]
    public void Application_does_not_reference_outer_layers_or_providers() =>
        ShouldPass(Types.InAssembly(Assemblies.Application).ShouldNot()
            .HaveDependencyOnAny(
                "MonitorCloud.Infrastructure", "MonitorCloud.Api", "MonitorCloud.AgentGateway",
                "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.Data.SqlClient", "Microsoft.AspNetCore", "Dapper")
            .GetResult());

    [Fact]
    public void Infrastructure_does_not_reference_the_api() =>
        ShouldPass(Types.InAssembly(Assemblies.Infrastructure).ShouldNot().HaveDependencyOn("MonitorCloud.Api").GetResult());

    [Fact]
    public void AgentGateway_does_not_reference_the_api_or_infrastructure() =>
        ShouldPass(Types.InAssembly(Assemblies.AgentGateway).ShouldNot()
            .HaveDependencyOnAny("MonitorCloud.Api", "MonitorCloud.Infrastructure")
            .GetResult());

    [Fact]
    public void Controllers_depend_only_on_the_sender_dtos_and_aspnetcore() =>
        ShouldPass(Types.InAssembly(Assemblies.Api).That().ResideInNamespace("MonitorCloud.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOnAny(
                "MonitorCloud.Infrastructure", "MonitorCloud.Domain", "Microsoft.EntityFrameworkCore", "Dapper",
                "MonitorCloud.Application.Abstractions.Persistence")
            .GetResult());
}
