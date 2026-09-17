<#
.SYNOPSIS
    Builds the DeploySeal nopCommerce plugin for one nopCommerce version and zips it.

.DESCRIPTION
    nopCommerce assemblies are not on NuGet, so a plugin has to be built inside a nopCommerce
    source checkout, exactly like the plugins that ship with nopCommerce. This script:

      1. ensures .nop/<version> holds a depth-1 clone of the tag in versions.json,
      2. copies src/DeploySeal.Nop.Widget.<ver> into <checkout>/src/Plugins/Nop.Plugin.Widgets.DeploySeal,
      3. builds it with SolutionDir pointing at the checkout (this also builds Nop.Web; the first
         run restores the whole nopCommerce solution and takes several minutes),
      4. verifies the plugin output folder holds only what a store needs,
      5. zips <checkout>/src/Presentation/Nop.Web/Plugins/Widgets.DeploySeal into
         artifacts/DeploySeal.Nop.Widget-<version>.zip (upload-ready for Admin > Local plugins).

.PARAMETER Version
    nopCommerce major.minor, e.g. 4.90. Must have an entry in versions.json AND a project folder.

.PARAMETER Configuration
    Release (default) or Debug.

.EXAMPLE
    .\build\build.ps1 -Version 4.90
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# --- 1. Resolve the version --------------------------------------------------------------------
$versions = Get-Content (Join-Path $PSScriptRoot "versions.json") -Raw | ConvertFrom-Json
$entry = $versions.PSObject.Properties[$Version]
if (-not $entry) {
    $known = ($versions.PSObject.Properties | ForEach-Object { $_.Name }) -join ", "
    throw "Unknown nopCommerce version '$Version'. Known versions: $known (build/versions.json)."
}
$tag = $entry.Value.tag
$tfm = $entry.Value.tfm

$projectDir = Join-Path $repo ("src\DeploySeal.Nop.Widget." + ($Version -replace "\.", ""))
if (-not (Test-Path (Join-Path $projectDir "Nop.Plugin.Widgets.DeploySeal.csproj"))) {
    throw "No plugin project for nopCommerce $Version yet. Expected $projectDir\Nop.Plugin.Widgets.DeploySeal.csproj (versions.json knows the tag '$tag', $tfm, but the port has not been written)."
}

# --- 2. Ensure the nopCommerce checkout --------------------------------------------------------
$nopDir = Join-Path $repo ".nop\$Version"
$nopWebProj = Join-Path $nopDir "src\Presentation\Nop.Web\Nop.Web.csproj"
if (-not (Test-Path $nopWebProj)) {
    Write-Host "Cloning nopCommerce $tag into $nopDir (depth 1)..."
    New-Item -ItemType Directory -Force (Split-Path $nopDir -Parent) | Out-Null
    & git clone --depth 1 --branch $tag https://github.com/nopSolutions/nopCommerce.git $nopDir
    if ($LASTEXITCODE -ne 0) { throw "git clone of $tag failed." }
}
$solutionDir = (Resolve-Path (Join-Path $nopDir "src")).Path
if (-not $solutionDir.EndsWith("\")) { $solutionDir += "\" }

# --- 3. Copy the project into the checkout and build it ---------------------------------------
$target = Join-Path $solutionDir "Plugins\Nop.Plugin.Widgets.DeploySeal"
Write-Host "Copying $projectDir -> $target"
& robocopy $projectDir $target /MIR /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

$pluginOut = Join-Path $solutionDir "Presentation\Nop.Web\Plugins\Widgets.DeploySeal"
if (Test-Path $pluginOut) { Remove-Item $pluginOut -Recurse -Force }

Write-Host "Building ($Configuration, $tfm) with SolutionDir=$solutionDir"
& dotnet build (Join-Path $target "Nop.Plugin.Widgets.DeploySeal.csproj") `
    -c $Configuration `
    -p:SolutionDir=$solutionDir `
    -p:DeploySealRepoRoot=$repo `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

# --- 4. Verify the output folder ---------------------------------------------------------------
if (-not (Test-Path (Join-Path $pluginOut "plugin.json"))) { throw "Build produced no plugin.json in $pluginOut" }
$files = Get-ChildItem $pluginOut -Recurse -File
$dlls = @($files | Where-Object { $_.Extension -eq ".dll" })
Write-Host "Plugin folder $pluginOut contains:"
$files | ForEach-Object { Write-Host ("  " + $_.FullName.Substring($pluginOut.Length + 1) + "  (" + $_.Length + " bytes)") }
if ($dlls.Count -ne 1 -or $dlls[0].Name -ne "Nop.Plugin.Widgets.DeploySeal.dll") {
    throw "Expected exactly one DLL (Nop.Plugin.Widgets.DeploySeal.dll) in the plugin folder; ClearPluginAssemblies did not run or the project pulled in an extra dependency. Found: " + (($dlls | ForEach-Object { $_.Name }) -join ", ")
}
$pluginJson = Get-Content (Join-Path $pluginOut "plugin.json") -Raw | ConvertFrom-Json
if ($pluginJson.SupportedVersions -notcontains $Version) {
    throw "plugin.json SupportedVersions ($($pluginJson.SupportedVersions -join ', ')) does not include $Version"
}

# --- 5. Zip ------------------------------------------------------------------------------------
if (-not $SkipZip) {
    $artifacts = Join-Path $repo "artifacts"
    New-Item -ItemType Directory -Force $artifacts | Out-Null
    $zip = Join-Path $artifacts "DeploySeal.Nop.Widget-$Version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    # The zip holds the plugin FOLDER (Widgets.DeploySeal/plugin.json ...), which is the layout
    # nopCommerce's "Upload plugin or theme" accepts and what you unzip into /Plugins by hand.
    Compress-Archive -Path $pluginOut -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Wrote $zip ($((Get-Item $zip).Length) bytes)"
}

Write-Host "Done: nopCommerce $Version ($tag, $tfm) plugin $($pluginJson.Version)"
