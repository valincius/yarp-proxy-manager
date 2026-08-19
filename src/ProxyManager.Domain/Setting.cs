namespace ProxyManager.Domain;

/// <summary>A single application setting stored as a key/value pair (e.g. 404 page mode/template).</summary>
public sealed class Setting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
