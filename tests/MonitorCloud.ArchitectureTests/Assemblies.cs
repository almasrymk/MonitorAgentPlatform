using System.Reflection;

namespace MonitorCloud.ArchitectureTests;

internal static class Assemblies
{
    public static readonly Assembly SharedKernel = typeof(MonitorCloud.SharedKernel.Entity).Assembly;
    public static readonly Assembly Domain = typeof(MonitorCloud.Domain.AssemblyMarker).Assembly;
    public static readonly Assembly Application = typeof(MonitorCloud.Application.DependencyInjection).Assembly;
    public static readonly Assembly Infrastructure = typeof(MonitorCloud.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly AgentGateway = Assembly.Load("MonitorCloud.AgentGateway");
    public static readonly Assembly Api = typeof(Program).Assembly;

    public static readonly Assembly[] Ours = [SharedKernel, Domain, Application, Infrastructure, AgentGateway, Api];

    /// <summary>Business modules (01 section 2). Messaging and Audit are shared and exempt from isolation.</summary>
    public static readonly string[] Modules =
        ["Identity", "Tenancy", "Licensing", "Devices", "Telemetry", "Monitoring", "Notifications", "Configuration", "Reports", "Archive", "Media", "Commands"];

    public static readonly string[] SharedModules = ["Audit", "Messaging"];
}
