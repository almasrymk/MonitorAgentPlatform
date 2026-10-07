using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.SimulatedAgent;

/// <summary>
/// A device speaking the real protocol over one gRPC stream (05 sections 2 and 11): Hello, Welcome, heartbeats,
/// Goodbye. Every received cloud message is kept for inspection.
/// </summary>
public sealed class SimulatedAgent(AgentIdentity identity, GrpcChannel channel) : IAsyncDisposable
{
    public const string AgentVersion = "1.1.0-sim";

    private readonly ConcurrentQueue<CloudMessage> _received = new();
    private readonly TaskCompletionSource<Welcome> _welcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AsyncDuplexStreamingCall<AgentMessage, CloudMessage>? _call;
    private Task? _receiveLoop;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private ulong _sequence;

    public AgentIdentity Identity { get; } = identity;

    public IReadOnlyCollection<CloudMessage> Received => [.. _received];

    /// <summary>The Disconnect the cloud sent, if any.</summary>
    public Disconnect? Disconnected { get; private set; }

    /// <summary>Completes when the cloud ends the stream (or it fails).</summary>
    public Task Completion => _receiveLoop ?? Task.CompletedTask;

    public uint HeartbeatSeconds { get; private set; } = 30;

    /// <summary>The gRPC status when the call failed (e.g. Unauthenticated for an expired token).</summary>
    public StatusCode? Failure { get; private set; }

    /// <summary>Opens the stream with the device token. Call <see cref="HelloAsync"/> next.</summary>
    public void Open(string token, CancellationToken cancellationToken = default)
    {
        var client = new AgentGateway.AgentGatewayClient(channel);
        _call = client.Connect(new Metadata { { "Authorization", $"Bearer {token}" } }, cancellationToken: cancellationToken);
        _receiveLoop = ReceiveAsync(_call, cancellationToken);
    }

    /// <summary>Opens the stream, sends Hello and waits for Welcome (or a Disconnect, then returns null).</summary>
    public async Task<Welcome?> ConnectAsync(string token, uint protocolVersion = 1, CancellationToken cancellationToken = default)
    {
        Open(token, cancellationToken);
        await HelloAsync(protocolVersion, cancellationToken);
        var finished = await Task.WhenAny(_welcome.Task, Completion);
        return finished == _welcome.Task ? await _welcome.Task : null;
    }

    public Task HelloAsync(uint protocolVersion = 1, CancellationToken cancellationToken = default) =>
        SendAsync(new AgentMessage
        {
            Hello = new Hello
            {
                ProtocolVersion = protocolVersion, AgentVersion = AgentVersion, Hostname = Identity.Hostname, OsFamily = OsFamily.Windows,
                OsName = "Windows Server 2022", OsVersion = "10.0.20348", Architecture = "x64", LocalIp = "10.20.0.10", MacAddress = "02-00-00-00-00-01",
                BootTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-3)),
            },
        }, cancellationToken);

    public Task HeartbeatAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new AgentMessage { Heartbeat = new Heartbeat { UptimeSeconds = 10_800, OutboxDepth = 0 } }, cancellationToken);

    public async Task GoodbyeAsync(GoodbyeReason reason = GoodbyeReason.ServiceStopping, CancellationToken cancellationToken = default)
    {
        await SendAsync(new AgentMessage { Goodbye = new Goodbye { Reason = reason } }, cancellationToken);
        await CloseAsync();
    }

    /// <summary>Sends a guaranteed message with the next sequence number.</summary>
    public Task SendGuaranteedAsync(AgentMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Sequence = Interlocked.Increment(ref _sequence);
        return SendAsync(message, cancellationToken);
    }

    public async Task SendAsync(AgentMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var call = _call ?? throw new InvalidOperationException("Open the stream first.");
        if (string.IsNullOrEmpty(message.MessageId))
            message.MessageId = Guid.NewGuid().ToString("N");
        message.SentAt ??= Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await call.RequestStream.WriteAsync(message, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Ends the request stream without Goodbye (as a crashed or disconnected agent would).</summary>
    public async Task CloseAsync()
    {
        if (_call is null)
            return;
        try
        {
            await _call.RequestStream.CompleteAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or RpcException)
        {
            // Already closed.
        }
    }

    /// <summary>Sends heartbeats until the stream ends or <paramref name="cancellationToken"/> fires.</summary>
    public async Task RunHeartbeatsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !Completion.IsCompleted)
        {
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), cancellationToken), Completion);
            if (cancellationToken.IsCancellationRequested || Completion.IsCompleted)
                return;
            await HeartbeatAsync(cancellationToken);
        }
    }

    private async Task ReceiveAsync(AsyncDuplexStreamingCall<AgentMessage, CloudMessage> call, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                _received.Enqueue(message);
                switch (message.BodyCase)
                {
                    case CloudMessage.BodyOneofCase.Welcome:
                        HeartbeatSeconds = message.Welcome.HeartbeatSeconds == 0 ? 30 : message.Welcome.HeartbeatSeconds;
                        _welcome.TrySetResult(message.Welcome);
                        break;
                    case CloudMessage.BodyOneofCase.Disconnect:
                        Disconnected = message.Disconnect;
                        break;
                }
            }
        }
        catch (RpcException ex)
        {
            // Unauthenticated, cancelled or the server went away.
            Failure = ex.StatusCode;
        }
        catch (OperationCanceledException)
        {
            // Stopped by the caller.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        if (_receiveLoop is not null)
            await Task.WhenAny(_receiveLoop, Task.Delay(TimeSpan.FromSeconds(2)));
        _call?.Dispose();
        _writeLock.Dispose();
    }
}
