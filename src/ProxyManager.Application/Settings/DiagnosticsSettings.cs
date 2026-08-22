namespace ProxyManager.Application.Settings;

/// <summary>Keys and defaults for the diagnostics capture settings.</summary>
public static class DiagnosticsSettings
{
    public const string CaptureEnabledKey = "Diagnostics:CaptureEnabled";
    public const string CaptureSizeKey = "Diagnostics:CaptureSize";
    public const int DefaultCaptureSize = 4096;
    public const int MaxCaptureSize = 1_000_000;

    public static int ParseSize(string? value) =>
        int.TryParse(value, out var size) && size is > 0 and <= MaxCaptureSize ? size : DefaultCaptureSize;
}

/// <summary>Opt-in request/response body capture for the diagnostics recent-requests view.</summary>
public sealed record DiagnosticsSettingsDto(bool CaptureEnabled, int CaptureSize);

public sealed record DiagnosticsSettingsInput(bool CaptureEnabled, int CaptureSize);
