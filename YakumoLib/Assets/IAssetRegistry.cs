namespace YakumoLib.Assets;

public interface IAssetRegistry
{
    void Register<T>(IAssetParser<T> parser);
    IAssetParser<T>? GetParser<T>(AssetEntry entry);
}