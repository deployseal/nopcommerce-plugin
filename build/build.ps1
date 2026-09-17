<#
.SYNOPSIS
    Builds the DeploySeal nopCommerce plugin for one nopCommerce version and zips it.

.DESCRIPTION
    nopCommerce assemblies are not on NuGet, so a plugin has to be built inside a nopCommerce
    source checkout, exactly like the plugins that ship with nopCommerce. This script:

      1. ensures .nop/<version> holds a depth-1 clone of the tag in versions.json,
      2. copies src/DeploySeal.Nop.Widget.<ver> into <checkout>/src/Plugins/Nop.Plugin.Widgets.DeploySeal,
      3. builds it with SolutionDir pointing at the checkout (this also builds Nop.Web; the first
         run restores the whole nopCommerce solution and takes several minutes). When the version's
         TFM has no matching .NET SDK on this machine (or -UseDocker is passed) the build step runs
         inside the version's "sdkImage" container from versions.json with the repository mounted
         at /work; everything else (clone, copy, verify, zip) stays on the host,
      4. verifies the plugin output folder holds only what a store needs,
      5. zips <checkout>/src/Presentation/Nop.Web/Plugins/Widgets.DeploySeal into
         artifacts/DeploySeal.Nop.Widget-<version>.zip (upload-ready for Admin > Local plugins).

.PARAMETER Version
    nopCommerce major.minor, e.g. 4.90. Must have an entry in versions.json AND a project folder.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER UseDocker
    Always run the build step in the version's SDK container, even when a matching SDK is installed.
    Without it the container is used only when no local SDK matches the TFM's major version.

.PARAMETER NuGetVolume
    Name of the Docker volume that caches NuGet packages between container builds (default
    deployseal-nuget). Only used for container builds.

.EXAMPLE
    .\build\build.ps1 -Version 4.90
.EXAMPLE
    .\build\build.ps1 -Version 4.60          # net7.0: builds in mcr.microsoft.com/dotnet/sdk:7.0 unless SDK 7 is installed
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$SkipZip,

    [switch]$UseDocker,

    [string]$NuGetVolume = "deployseal-nuget"
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
$sdkImage = $entry.Value.sdkImage

# --- 1b. Decide where the build step runs --------------------------------------------------------
# A TFM "netN.0" needs an SDK whose major version is N (or newer with roll-forward, but nopCommerce
# pins global.json to its own major, so "same major" is the honest test).
$tfmMajor = [int]([regex]::Match($tfm, "^net(\d+)\.").Groups[1].Value)
$localSdkMajors = @()
try {
    $localSdkMajors = @(& dotnet --list-sdks 2>$null | ForEach-Object { [int](($_ -split "\s+")[0].Split(".")[0]) } | Sort-Object -Unique)
} catch { }
$haveLocalSdk = $localSdkMajors -contains $tfmMajor
$buildInDocker = $UseDocker.IsPresent -or -not $haveLocalSdk
if ($buildInDocker) {
    if (-not $sdkImage) { throw "nopCommerce $Version ($tfm) needs .NET SDK $tfmMajor, which is not installed (found: $($localSdkMajors -join ', ')), and versions.json has no 'sdkImage' for it." }
    & docker version --format "{{.Server.Version}}" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "nopCommerce $Version ($tfm) needs .NET SDK $tfmMajor, which is not installed (found: $($localSdkMajors -join ', ')), and Docker is not available to build inside $sdkImage." }
    Write-Host "Build step will run inside $sdkImage (local SDK majors: $($localSdkMajors -join ', '); UseDocker=$($UseDocker.IsPresent))"
}

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

$buildStarted = Get-Date
if ($buildInDocker) {
    # Inside the container the repository is /work, so every path handed to MSBuild is re-rooted.
    # The plugin project is built on its own (its ProjectReference pulls in Nop.Web and the
    # libraries it needs; the rest of the nopCommerce solution is never restored). The NuGet cache
    # lives in a named volume so the second build does not download the packages again. obj/ and
    # bin/ land in the bind-mounted checkout like a host build's would.
    $containerSolutionDir = "/work/.nop/$Version/src/"
    $containerProject = "/work/.nop/$Version/src/Plugins/Nop.Plugin.Widgets.DeploySeal/Nop.Plugin.Widgets.DeploySeal.csproj"
    Write-Host "Building ($Configuration, $tfm) in $sdkImage with SolutionDir=$containerSolutionDir"
    & docker run --rm `
        -v "${repo}:/work" `
        -v "${NuGetVolume}:/root/.nuget/packages" `
        -w /work `
        -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 `
        $sdkImage `
        dotnet build $containerProject -c $Configuration "-p:SolutionDir=$containerSolutionDir" "-p:DeploySealRepoRoot=/work" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build (in $sdkImage) failed." }
} else {
    Write-Host "Building ($Configuration, $tfm) with SolutionDir=$solutionDir"
    & dotnet build (Join-Path $target "Nop.Plugin.Widgets.DeploySeal.csproj") `
        -c $Configuration `
        -p:SolutionDir=$solutionDir `
        -p:DeploySealRepoRoot=$repo `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
}
Write-Host ("Build step took {0:n0} s" -f ((Get-Date) - $buildStarted).TotalSeconds)

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

$where = if ($buildInDocker) { "built in $sdkImage" } else { "built with the local SDK" }
Write-Host "Done: nopCommerce $Version ($tag, $tfm) plugin $($pluginJson.Version), $where"
