using System.Text.RegularExpressions;

namespace DeploySeal.Nop.Core;

/// <summary>
/// Reads and validates a commit SHA written by a deploy pipeline (contract §4 rule 1).
/// </summary>
public static partial class GitSha
{
    [GeneratedRegex("^[0-9a-f]{7,40}$")]
    private static partial Regex ShaPattern();

    /// <summary>
    /// Normalises file content into a SHA: first non-empty line, first whitespace-separated token,
    /// lower-cased, 7–40 hex chars. Returns null otherwise. Taking the first token only also
    /// accepts the common "sha1 (message)" deploy-log shape.
    /// </summary>
    public static string? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var line = content.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null)
            return null;

        var token = line.Split(' ', '\t')[0].Trim().ToLowerInvariant();
        return ShaPattern().IsMatch(token) ? token : null;
    }

    /// <summary>Reads and parses a SHA file; null when the path is empty, missing or malformed. Never throws.</summary>
    public static string? TryReadFile(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return null;

        try
        {
            return File.Exists(absolutePath) ? Parse(File.ReadAllText(absolutePath)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The first 7 characters, the form the contract uses in "4.90.8+a1b2c3d".</summary>
    public static string Short(string sha) =>
        sha.Length <= DeploySealContract.ShortShaLength ? sha : sha[..DeploySealContract.ShortShaLength];
}
