<#
.SYNOPSIS
    Packs the four per-version plugin zips into one Marketplace-style, multi-version bundle.

.DESCRIPTION
    The nopCommerce Marketplace's "Upload plugin or theme" action (Nop.Services.Plugins.UploadService)
    accepts an archive with a single plugin folder at its root, OR an archive with an
    "uploadedItems.json" file at its root describing several items, each pointing at its own
    folder inside the archive. This script builds the second shape from build/versions.json and
    artifacts/DeploySeal.Nop.Widget-<version>.zip (produced by build/build.ps1), so one upload on
    any supported nopCommerce version installs the right build automatically.

    uploadedItems.json schema (from UploadService.UploadedItem, matching the Type/SupportedVersion/
    DirectoryPath/SystemName/SourceDirectoryPath property names nopCommerce deserializes with
    Newtonsoft.Json -- see .nop/<ver>/src/Libraries/Nop.Services/Plugins/UploadService.cs and its
    Samples/uploadedItems.json):

      [
        {
          "Type": "Plugin",
          "SupportedVersion": "4.90",
          "DirectoryPath": "4.90/Widgets.DeploySeal/",
          "SystemName": "Widgets.DeploySeal"
        },
        ...
      ]

    nopCommerce picks the entry whose SupportedVersion is a substring of NopVersion.CURRENT_VERSION
    (a plain string.Contains, so "4.90" matches only nopCommerce 4.90.x) and extracts everything
    under that entry's DirectoryPath into Plugins/<last path segment> (here, Plugins/Widgets.DeploySeal).

    Bundle layout produced:

      uploadedItems.json
      4.60/Widgets.DeploySeal/plugin.json, Nop.Plugin.Widgets.DeploySeal.dll, ...
      4.70/Widgets.DeploySeal/...
      4.80/Widgets.DeploySeal/...
      4.90/Widgets.DeploySeal/...

.PARAMETER OutFile
    Where to write the bundle (default artifacts/DeploySeal.Nop.Widget-all-versions.zip).

.EXAMPLE
    .\build\bundle.ps1
#>
[CmdletBinding()]
param(
    [string]$OutFile
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifacts = Join-Path $repo "artifacts"
if (-not $OutFile) { $OutFile = Join-Path $artifacts "DeploySeal.Nop.Widget-all-versions.zip" }

$versions = Get-Content (Join-Path $PSScriptRoot "versions.json") -Raw | ConvertFrom-Json
$versionNames = $versions.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("deployseal-bundle-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $staging | Out-Null
try {
    $items = @()
    foreach ($version in $versionNames) {
        $zip = Join-Path $artifacts "DeploySeal.Nop.Widget-$version.zip"
        if (-not (Test-Path $zip)) { throw "Missing $zip -- run build/build.ps1 -Version $version first." }

        # Each per-version zip already holds exactly the plugin folder at its root (see build.ps1
        # step 5: it zips .../Plugins/Widgets.DeploySeal). Extract it under a version-named folder
        # in the staging area so the bundle can tell versions apart.
        $versionDir = Join-Path $staging $version
        New-Item -ItemType Directory -Force $versionDir | Out-Null
        Expand-Archive -Path $zip -DestinationPath $versionDir -Force

        $pluginDirs = Get-ChildItem $versionDir -Directory
        if ($pluginDirs.Count -ne 1) { throw "$zip did not extract to exactly one root folder (found: $(($pluginDirs | ForEach-Object Name) -join ', '))." }
        $pluginFolderName = $pluginDirs[0].Name
        $pluginJsonPath = Join-Path $pluginDirs[0].FullName "plugin.json"
        if (-not (Test-Path $pluginJsonPath)) { throw "$zip has no plugin.json under $pluginFolderName." }
        $pluginJson = Get-Content $pluginJsonPath -Raw | ConvertFrom-Json
        if ($pluginJson.SystemName -ne "Widgets.DeploySeal") { throw "$zip plugin.json SystemName is '$($pluginJson.SystemName)', expected Widgets.DeploySeal." }

        $items += [ordered]@{
            Type              = "Plugin"
            SupportedVersion  = $version
            DirectoryPath     = "$version/$pluginFolderName/"
            SystemName        = $pluginJson.SystemName
        }
        Write-Host "Staged $version -> $version/$pluginFolderName/ (plugin.json Version $($pluginJson.Version))"
    }

    $manifestPath = Join-Path $staging "uploadedItems.json"
    # UploadService reads this with Newtonsoft's default array formatting; a plain ConvertTo-Json
    # (PascalCase properties already match [JsonProperty] names) is exactly the shape it expects.
    $manifestJson = $items | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Wrote $manifestPath"

    New-Item -ItemType Directory -Force $artifacts | Out-Null
    if (Test-Path $OutFile) { Remove-Item $OutFile -Force }

    # Zip the CONTENTS of $staging (uploadedItems.json + the four version folders) at the archive
    # root -- not $staging itself as a subfolder -- because UploadService looks for
    # "uploadedItems.json" with no directory component in its FullName. Built with
    # System.IO.Compression.ZipArchive directly (rather than Compress-Archive) because
    # Compress-Archive on Windows PowerShell writes entry names with BACKSLASH path separators,
    # which is not a valid zip entry name (the zip spec requires "/") and breaks UploadService's
    # `entry.FullName.Split('/')` / `StartsWith(itemPath)` logic used to find uploadedItems.json
    # and each version's DirectoryPath.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [System.IO.File]::Open($OutFile, [System.IO.FileMode]::Create)
    try {
        $zipArchive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $files = Get-ChildItem -Path $staging -Recurse -File
            foreach ($file in $files) {
                $relPath = $file.FullName.Substring($staging.Length + 1) -replace '\\', '/'
                $entry = $zipArchive.CreateEntry($relPath, [System.IO.Compression.CompressionLevel]::Optimal)
                $entryStream = $entry.Open()
                try {
                    $fileStream = [System.IO.File]::OpenRead($file.FullName)
                    try { $fileStream.CopyTo($entryStream) } finally { $fileStream.Dispose() }
                } finally {
                    $entryStream.Dispose()
                }
            }
        } finally {
            $zipArchive.Dispose()
        }
    } finally {
        $zipStream.Dispose()
    }

    $sizeBytes = (Get-Item $OutFile).Length
    $sizeMb = [Math]::Round($sizeBytes / 1MB, 2)
    Write-Host "Wrote $OutFile ($sizeBytes bytes, $sizeMb MB)"
    if ($sizeBytes -gt 10MB) { throw "$OutFile is $sizeMb MB, over the Marketplace's 10 MB limit." }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
