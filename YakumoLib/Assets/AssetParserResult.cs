namespace YakumoLib.Assets;

public readonly record struct AssetParseResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public string? Error { get; }

    private AssetParseResult(bool success, T? value, string? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }
    public static AssetParseResult<T> Ok(T value) => new(true, value, null);
    public static AssetParseResult<T> Fail(string error) => new(false, default, error);
}