using Grpc.Core;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.AgentGateway.Transport;

/// <summary>
/// One device stream. Session logic, routing and presence use only this interface, so another transport (e.g. a
/// WebSocket carrying the same envelopes, 05 section 12) can be added without touching them.
/// </summary>
public interface IAgentTransport
{
    /// <summary>The next agent message, or null when the agent closed the stream.</summary>
    ValueTask<AgentMessage?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes are serialised: several parts of the gateway may send on one stream.</summary>
    ValueTask WriteAsync(CloudMessage message, CancellationToken cancellationToken);

    string? RemoteAddress { get; }
}

/// <summary>The gRPC adapter over the bidirectional <c>Connect</c> call.</summary>
public sealed class GrpcAgentTransport(IAsyncStreamReader<AgentMessage> reader, IServerStreamWriter<CloudMessage> writer, string? remoteAddress) : IAgentTransport, IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string? RemoteAddress { get; } = remoteAddress;

    public async ValueTask<AgentMessage?> ReadAsync(CancellationToken cancellationToken) =>
        await reader.MoveNext(cancellationToken) ? reader.Current : null;

    public async ValueTask WriteAsync(CloudMessage message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteAsync(message, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();
}
