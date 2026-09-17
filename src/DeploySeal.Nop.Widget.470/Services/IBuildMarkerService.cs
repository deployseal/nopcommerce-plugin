using DeploySeal.Nop.Core;

namespace Nop.Plugin.Widgets.DeploySeal.Services;

/// <summary>Resolves the build marker for a given settings scope. Scoped per request.</summary>
public interface IBuildMarkerService
{
    /// <summary>The nopCommerce version this plugin is running inside ("4.90.8").</summary>
    string PlatformVersion { get; }

    /// <summary>Absolute path the SHA file setting resolves to, or null when the setting is empty.</summary>
    string? ResolveShaFilePath(string? gitShaFilePath);

    /// <summary>Reads the SHA the settings point at; null when there is none. Cached for the request.</summary>
    string? ReadGitSha(string? gitShaFilePath);

    /// <summary>Resolves the marker for these settings. Cached for the request per settings instance.</summary>
    BuildMarkerResult Resolve(DeploySealWidgetOptions options);
}
