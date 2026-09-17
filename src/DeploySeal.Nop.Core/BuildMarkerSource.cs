namespace DeploySeal.Nop.Core;

/// <summary>
/// Where the plugin takes the build marker from (contract §4 rule 4). Stored as an int in
/// nopCommerce settings, so the numeric values are part of the plugin's persisted state.
/// </summary>
public enum BuildMarkerSource
{
    /// <summary>The nopCommerce version alone, e.g. "4.90.8".</summary>
    NopVersion = 0,

    /// <summary>
    /// nopCommerce version plus a short git SHA when one can be read, e.g. "4.90.8+a1b2c3d".
    /// Degrades to <see cref="NopVersion"/> when no SHA is readable. This is the install default.
    /// </summary>
    NopVersionPlusGitSha = 1,

    /// <summary>The git SHA on its own (contract rule 1: "prefer the commit"). Emits nothing when unreadable.</summary>
    GitSha = 2,

    /// <summary>A marker typed by the installer.</summary>
    Manual = 3,
}
