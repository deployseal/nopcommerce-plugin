using DeploySeal.Nop.Core;
using Nop.Core;
using Nop.Core.Infrastructure;

namespace Nop.Plugin.Widgets.DeploySeal.Services;

/// <summary>
/// The nopCommerce-specific inputs to <see cref="BuildMarkerResolver"/>: the platform version
/// constant and a SHA file resolved against the application root.
/// </summary>
public class BuildMarkerService : IBuildMarkerService
{
    private readonly INopFileProvider _fileProvider;
    private readonly Dictionary<string, string?> _shaCache = new(StringComparer.Ordinal);
    private BuildMarkerResult? _lastResult;
    private string? _lastKey;

    public BuildMarkerService(INopFileProvider fileProvider)
    {
        _fileProvider = fileProvider;
    }

    /// <inheritdoc />
    public string PlatformVersion => NopVersion.FULL_VERSION;

    /// <inheritdoc />
    public string? ResolveShaFilePath(string? gitShaFilePath)
    {
        if (string.IsNullOrWhiteSpace(gitShaFilePath))
            return null;

        var path = gitShaFilePath.Trim();
        if (Path.IsPathRooted(path))
            return path;

        // Relative to the application root (the folder Nop.Web.dll runs from), "~/" tolerated.
        return _fileProvider.MapPath(path);
    }

    /// <inheritdoc />
    public string? ReadGitSha(string? gitShaFilePath)
    {
        var absolute = ResolveShaFilePath(gitShaFilePath);
        if (absolute is null)
            return null;

        if (_shaCache.TryGetValue(absolute, out var cached))
            return cached;

        var sha = GitSha.TryReadFile(absolute);
        _shaCache[absolute] = sha;
        return sha;
    }

    /// <inheritdoc />
    public BuildMarkerResult Resolve(DeploySealWidgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var key = $"{options.BuildMarkerSourceId}|{options.GitShaFilePath}|{options.ManualBuildMarker}";
        if (_lastResult is not null && _lastKey == key)
            return _lastResult;

        var sha = options.BuildMarkerSource is BuildMarkerSource.NopVersion or BuildMarkerSource.Manual
            ? null
            : ReadGitSha(options.GitShaFilePath);

        _lastResult = BuildMarkerResolver.Resolve(options.BuildMarkerSource, PlatformVersion, sha, options.ManualBuildMarker);
        _lastKey = key;
        return _lastResult;
    }
}
