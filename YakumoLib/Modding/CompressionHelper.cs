namespace YakumoLib.Modding;

/// <summary>Explicit GDeflate capability surface. It never disguises uncompressed bytes as compressed output.</summary>
public static class CompressionHelper
{
    public static CompressionResult Compress(byte[] data, string assetName = "unknown")
    {
        ArgumentNullException.ThrowIfNull(data);
        throw new NotSupportedException(
            $"GDeflate compression is unavailable for '{assetName}'. Disable compression to emit an honest uncompressed patch.");
    }

    public static int GetStorageSize(CompressionResult result) =>
        result.IsCompressed ? result.CompressedData.Length : result.OriginalSize;
}

public struct CompressionResult
{
    public byte[] CompressedData { get; set; }
    public int CompressedSize { get; set; }
    public int OriginalSize { get; set; }
    public bool IsCompressed { get; set; }
    public CompressionMethod Method { get; set; }
}

public enum CompressionMethod
{
    Uncompressed,
    GDeflate
}
