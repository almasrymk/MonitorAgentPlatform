namespace MonitorCloud.Application.Abstractions.Serialization;

/// <summary>Brotli-compressed UTF-8 JSON of inventory documents and snapshots (implemented in Infrastructure).</summary>
public interface IInventoryCodec
{
    string Decode(byte[] compressed);
}
