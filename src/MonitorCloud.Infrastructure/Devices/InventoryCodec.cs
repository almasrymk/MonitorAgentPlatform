using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MonitorCloud.Infrastructure.Devices;

/// <summary>Inventory documents are stored Brotli-compressed with the SHA-256 of the uncompressed JSON (02 section 4).</summary>
public static class InventoryCodec
{
    public static (byte[] Compressed, string Hash) Encode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(bytes);
        return (output.ToArray(), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public static string Decode(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        if (compressed.Length == 0)
            return string.Empty;
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(brotli, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
