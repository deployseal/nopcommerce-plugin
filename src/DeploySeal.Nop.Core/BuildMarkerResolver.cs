namespace DeploySeal.Nop.Core;

/// <summary>Outcome of resolving the build marker: what will be emitted and why.</summary>
public sealed record BuildMarkerResult(string Marker, BuildMarkerSource RequestedSource, string SourceDescription, string? Warning)
{
    /// <summary>True when a marker will be emitted (the attribute is omitted otherwise).</summary>
    public bool HasMarker => Marker.Length > 0;
}

/// <summary>
/// Composes the build marker from the configured source (contract §4). Pure: the caller supplies
/// the platform version and whatever SHA it managed to read, so this runs identically on every
/// nopCommerce version and in unit tests.
/// </summary>
public static class BuildMarkerResolver
{
    public static BuildMarkerResult Resolve(BuildMarkerSource source, string? platformVersion, string? gitSha, string? manualMarker)
    {
        var version = Sanitize(platformVersion);
        var sha = gitSha is null ? null : GitSha.Parse(gitSha);

        switch (source)
        {
            case BuildMarkerSource.NopVersion:
                return new BuildMarkerResult(Clamp(version), source, "nopCommerce version", null);

            case BuildMarkerSource.NopVersionPlusGitSha:
                if (sha is null)
                    return new BuildMarkerResult(Clamp(version), source,
                        "nopCommerce version (no git SHA could be read, so there is no \"+sha\" suffix)",
                        "No git SHA could be read. Point \"Git SHA file path\" at a file your deploy writes the commit into; until then the marker will not change between deployments of the same nopCommerce version.");
                return new BuildMarkerResult(Clamp($"{version}+{GitSha.Short(sha)}"), source, "nopCommerce version + short git SHA", null);

            case BuildMarkerSource.GitSha:
                if (sha is null)
                    return new BuildMarkerResult(string.Empty, source,
                        "git SHA (not readable, so no marker is emitted)",
                        "The build marker source is \"Git SHA\" but no SHA could be read. The data-ds-build attribute is omitted until the file exists and holds a 7–40 character hex SHA.");
                return new BuildMarkerResult(Clamp(sha), source, "git SHA", null);

            case BuildMarkerSource.Manual:
                var manual = Sanitize(manualMarker);
                if (manual.Length == 0)
                    return new BuildMarkerResult(string.Empty, source,
                        "manual (empty, so no marker is emitted)",
                        "The build marker source is \"Manual\" but the manual marker is empty. The data-ds-build attribute is omitted.");
                return new BuildMarkerResult(Clamp(manual), source, "manual", null);

            default:
                return Resolve(BuildMarkerSource.NopVersionPlusGitSha, platformVersion, gitSha, manualMarker);
        }
    }

    /// <summary>Contract §4 rule 3: single token, no whitespace. Whitespace and control characters are removed, not replaced.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Concat(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)));
    }

    /// <summary>Contract §2/§4: ≤ 64 characters.</summary>
    public static string Clamp(string marker) =>
        marker.Length <= DeploySealContract.BuildMarkerMaxLength ? marker : marker[..DeploySealContract.BuildMarkerMaxLength];
}
