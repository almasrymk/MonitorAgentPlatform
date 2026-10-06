using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.GatewayTests;

/// <summary>Guards the generated device protocol until the gateway exists (M4).</summary>
public sealed class ProtocolContractTests
{
    [Fact]
    public void Protocol_package_is_monitor_agent_v1() =>
        AgentReflection.Descriptor.Package.ShouldBe("monitor.agent.v1");

    [Fact]
    public void Gateway_service_has_one_bidirectional_connect_method()
    {
        var service = AgentReflection.Descriptor.Services.ShouldHaveSingleItem();
        service.Name.ShouldBe("AgentGateway");
        var method = service.Methods.ShouldHaveSingleItem();
        method.Name.ShouldBe("Connect");
        method.IsClientStreaming.ShouldBeTrue();
        method.IsServerStreaming.ShouldBeTrue();
    }

    [Fact]
    public void First_agent_message_type_is_hello()
    {
        var message = new AgentMessage { Hello = new Hello() };

        message.BodyCase.ShouldBe(AgentMessage.BodyOneofCase.Hello);
    }
}
