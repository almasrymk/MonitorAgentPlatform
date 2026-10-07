using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentGateway.Transport;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.AgentGateway;

/// <summary>The gRPC <c>AgentGateway.Connect</c> stream: a thin adapter over <see cref="AgentSessionHandler"/>.</summary>
[Authorize(Policy = AgentGatewayRegistration.DevicePolicy)]
public sealed class AgentGatewayService(AgentSessionHandler sessions) : AgentProtocol.V1.AgentGateway.AgentGatewayBase
{
    public override async Task Connect(IAsyncStreamReader<AgentMessage> requestStream, IServerStreamWriter<CloudMessage> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.GetHttpContext().User;
        // The device and tenant come from the token, never from a message.
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var deviceId) || !Guid.TryParse(user.FindFirst("tid")?.Value, out var tenantId))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "A device token is required."));

        using var transport = new GrpcAgentTransport(requestStream, responseStream, context.GetHttpContext().Connection.RemoteIpAddress?.ToString());
        await sessions.RunAsync(transport, deviceId, tenantId, context.CancellationToken);
    }
}
