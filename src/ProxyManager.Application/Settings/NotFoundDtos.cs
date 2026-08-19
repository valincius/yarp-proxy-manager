namespace ProxyManager.Application.Settings;

/// <summary>How the proxy port responds to requests with no matching route.</summary>
public static class NotFoundModes
{
    public const string Default = "Default";
    public const string Empty = "Empty";
    public const string Custom = "Custom";

    public static readonly string[] All = [Default, Empty, Custom];
}

/// <summary>The 404-page configuration.</summary>
public sealed record NotFoundSettingsDto(string Mode, string Template);

/// <summary>Input for updating the 404-page configuration. Template is only stored for Custom mode.</summary>
public sealed record NotFoundSettingsInput(string Mode, string? Template);
