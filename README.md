# DeploySeal widget for nopCommerce

A nopCommerce widget plugin that loads the [DeploySeal](https://deployseal.com) tester widget on
the storefront and declares, per store, **which environment** the page belongs to and **which
build** is running, so the readiness report can prove what was tested. It is one of the
DeploySeal install paths and obeys the DeploySeal install contract to the byte: the tag it emits is

```html
<script src="https://cdn.deployseal.com/ds-widget.js" data-ds-site-key="ls_…" data-ds-environment="staging" data-ds-build="4.90.8+a1b2c3d" async></script>
```

placed in `<head>`, once, only on the public storefront (never in `/Admin` unless you opt in).

## Supported nopCommerce versions

| nopCommerce | Tag             | .NET    | Plugin project                  | Status                     |
|-------------|-----------------|---------|---------------------------------|----------------------------|
| 4.60        | release-4.60.6  | net7.0  | `src/DeploySeal.Nop.Widget.460` | **verified end to end** (built in the .NET 7 SDK container) |
| 4.90        | release-4.90.8  | net9.0  | `src/DeploySeal.Nop.Widget.490` | **reference build, verified end to end** |
| 4.80        | release-4.80.9  | net9.0  | `src/DeploySeal.Nop.Widget.480` | planned                    |
| 4.70        | release-4.70.5  | net8.0  | `src/DeploySeal.Nop.Widget.470` | planned                    |

`build/versions.json` is the single list of versions the build knows about (tag, TFM and the SDK
container image used when that SDK is not installed locally).

The plugin behaves identically on every supported version: same settings, same Configure page,
same tag. The only visible difference is the build marker's default, which is that version's
`NopVersion.FULL_VERSION` (`4.60.6`, `4.90.8`).

## Install (store administrator)

1. Download `DeploySeal.Nop.Widget-<version>.zip` for your nopCommerce version.
2. Administration → Configuration → Local plugins → **Upload plugin or theme**, pick the zip
   (or unzip it so that `Plugins/Widgets.DeploySeal/plugin.json` exists), then **Install** and
   **Apply changes** (nopCommerce restarts).
3. Open the plugin's **Configure** page. It shows, for each store, the exact origins to register,
   the environment label it will declare, the exact build marker it will emit and where that comes
   from, and the tag it will render.
4. In DeploySeal: create an environment for the store, register the origins shown, copy the
   environment's public key into **Site key**, tick **Enabled**, **Save**.
5. Load the storefront once; the environment shows as *Live* in DeploySeal.

## Settings

All settings are overridable per store (multi-store: one key per environment per store).

| Setting | Default | Meaning |
|---|---|---|
| Enabled | off | Master switch; nothing renders while off. |
| Site key | empty | The environment's public key (`ls_…`; legacy `ds_…` keys still work). Not a secret: it only works from the environment's registered origins. |
| Environment label | guessed from the store URL: `staging` when the host contains staging/uat/test/dev/qa/sandbox/preprod/localhost, else `production` | Declared label, `[a-z0-9-]`, ≤ 32. Saved slugged; an empty value falls back to the guess. |
| Build marker source | nopCommerce version + git SHA when available | One of: nopCommerce version (`4.90.8`), nopCommerce version + git SHA (`4.90.8+a1b2c3d`, degrades to the version alone when no SHA can be read), git SHA only (emits nothing when unreadable), manual. |
| Git SHA file path | empty | File holding the deployed commit SHA (first line, 7–40 hex chars). Relative paths resolve from the application root, e.g. `App_Data/build-sha.txt`. Have your deploy pipeline write it. |
| Manual build marker | empty | Used by the manual source. One token, no whitespace, ≤ 64. |
| Script host (advanced) | `https://cdn.deployseal.com` | The only supported host; leave it. |
| Also load in the admin area | off | The admin layout has no head zone, so this uses the first admin body zone. |

The build marker is resolved once per request. Whatever it resolves to is printed on the Configure
page, verbatim, so it can be copied into a campaign's release identifier (`a1b2c3d` does **not**
confirm `4.90.8+a1b2c3d`; prefix matching runs from the start of the string).

## Repository layout

```
src/DeploySeal.Nop.Core/          pure logic, no nopCommerce references, multi-targets net7.0;net9.0, C# 11:
                                  settings POCO, EnvironmentLabel (slug + guess), OriginFormatter,
                                  GitSha, BuildMarkerResolver, SnippetBuilder, contract constants
src/DeploySeal.Nop.Widget.Shared/ what is byte-identical across nopCommerce versions and linked into every
                                  plugin project: the three Razor views (Configure, PublicInfo, _ViewImports)
                                  and logo.png
src/DeploySeal.Nop.Widget.490/    the 4.90 plugin: plugin class, settings, admin controller, view component,
                                  route + DI startup, plugin.json
src/DeploySeal.Nop.Widget.460/    the 4.60 plugin (net7.0): same files minus the RouteProvider, with the
                                  4.60 API spellings (see "nopCommerce 4.60 notes")
tests/DeploySeal.Nop.Core.Tests/  xunit tests for Core (slug, origins, marker composition/limits, byte-exact tag)
tests/e2e/verify.mjs              Playwright script that drives a real nopCommerce in Docker end to end
build/build.ps1                   clone nop tag → copy project in → build (locally or in the SDK container) → verify folder → zip
build/versions.json               nop version → tag / TFM / SDK container image
build/gen-logo.py                 regenerates logo.png from the product's seal outlines (Pillow only)
docker/docker-compose.4.60.yml    nopcommerceteam/nopcommerce:4.60.6 + PostgreSQL 15, port 8060, plugin folder bind-mounted
docker/docker-compose.4.90.yml    nopcommerceteam/nopcommerce:4.90.8 + PostgreSQL 17, port 8090, plugin folder bind-mounted
```

The Core sources are compiled *into* the plugin assembly (`<Compile Include>` in the plugin csproj),
so a store ships exactly one DLL and each nopCommerce version gets a plugin built against its own
TFM. Core is kept to C# 11 / net7.0 APIs so the 4.60 port compiles the same files unchanged. The
views and the logo are linked from `src/DeploySeal.Nop.Widget.Shared` the same way (`<Content
Link>`), so there is one copy of the Configure page; the per-version folders hold only C# and
`plugin.json`.

## Build

Prerequisites: git, PowerShell, Docker (for the end-to-end check and for any version whose .NET SDK
is not installed), and the .NET SDK matching the version's TFM for a native build (SDK 9 for 4.90 and
4.80). No SDK has to be installed for the older versions: see "Building in the SDK container".

```powershell
.\build\build.ps1 -Version 4.90
.\build\build.ps1 -Version 4.60
```

This clones the version's tag into `.nop/<ver>` (depth 1, first time only; nopCommerce assemblies are
not on NuGet so the plugin must be built inside a checkout), copies
`src/DeploySeal.Nop.Widget.<ver>` to `.nop/<ver>/src/Plugins/Nop.Plugin.Widgets.DeploySeal`, builds it
(this also builds Nop.Web; budget several minutes the first time), checks the output folder holds one
DLL + plugin.json + Views + logo, and zips it to `artifacts/DeploySeal.Nop.Widget-<ver>.zip`. It fails
clearly for versions that have a `versions.json` entry but no project yet.

### Building in the SDK container

nopCommerce 4.60 is net7.0 and 4.70 is net8.0; those SDKs are end of life and are not expected on a
developer machine. When `dotnet --list-sdks` shows no SDK with the TFM's major version (or when
`-UseDocker` is passed), `build.ps1` runs **only the build step** inside the version's `sdkImage`
from `versions.json` (`mcr.microsoft.com/dotnet/sdk:7.0` for 4.60) with the repository mounted at
`/work` and a named volume (`deployseal-nuget`) caching NuGet packages between runs. Clone, copy,
folder verification and zip stay on the host, and the plugin folder lands in the same place
(`.nop/<ver>/src/Presentation/Nop.Web/Plugins/Widgets.DeploySeal`), so the compose files and
`verify.mjs` do not care which way it was built. `obj/` and `bin/` are written into the bind-mounted
checkout, so a later host build of the same version (should you install that SDK) starts from a
Linux-produced `obj/`; delete `.nop/<ver>/src/**/obj` first in that case. Measured on Windows with
Docker Desktop (bind mount, cold NuGet cache): 4 min 30 s for the first 4.60 build (restore of the
Nop.Web dependency tree took 1 min 15 s of that; the rest is compiling Nop.Web and its libraries).
The script prints "Build step took N s" so the first run is not mistaken for a hang.

Unit tests (Core multi-targets net7.0 and net9.0; the test project runs on net9.0 by default and on
whatever `DeploySealTestTfm` names):

```powershell
dotnet test tests/DeploySeal.Nop.Core.Tests
# the net7.0 build of Core, in the same container the 4.60 plugin is built in:
docker run --rm -v "${PWD}:/work" -v deployseal-nuget:/root/.nuget/packages -w /work mcr.microsoft.com/dotnet/sdk:7.0 `
  dotnet test tests/DeploySeal.Nop.Core.Tests -p:DeploySealTestTfm=net7.0
```

End to end (real nopCommerce in Docker, PostgreSQL, install wizard, plugin install, configure,
storefront/admin assertions, SHA-file marker, screenshot to `artifacts/configure-<ver>.png`):

```powershell
.\build\build.ps1 -Version 4.60                                             # or 4.90
docker compose -f docker/docker-compose.4.60.yml up -d                      # port 8060 (4.90: 8090)
$env:DS_E2E_NODE_MODULES = "<a node_modules folder that has playwright>"   # or npm i playwright in tests/e2e
node tests/e2e/verify.mjs 4.60
docker compose -f docker/docker-compose.4.60.yml down -v
```

`verify.mjs <ver>` takes the expected full version from the tag in `versions.json`, the port from the
version digits (`80` + `60`) and the container name from the compose project name
(`deployseal-nop460-nop-1`), all overridable with `DS_NOP_VERSION`, `DS_NOP_URL`, `DS_NOP_CONTAINER`
(empty skips the SHA-file scenario). The same script drives 4.60 and 4.90: every selector it touches
(install wizard, storefront login, Local plugins grid, Apply changes, the plugin's Configure page) is
the same in both.

## nopCommerce 4.60 notes

What the 4.60 project does differently from the 4.90 reference, all forced by the 4.60 API surface:

- `AreaNames.Admin` (4.90: `AreaNames.ADMIN`); the value is `"Admin"` in both.
- No `INopUrlHelper` and no named plugin route: `GetConfigurationPageUrl()` returns
  `IWebHelper.GetStoreLocation() + "Admin/WidgetsDeploySeal/Configure"` and the default
  `{area:exists}/{controller}/{action}` route serves it, so there is no `RouteProvider` (exactly like
  the 4.60 Google Analytics plugin).
- No `[CheckPermission]` attribute: both `Configure` actions call
  `IPermissionService.AuthorizeAsync(StandardPermissionProvider.ManageWidgets)` and return
  `AccessDeniedView()`.
- csproj mirrors the 4.60 Google Analytics one: relative `..\..\Presentation\Nop.Web\Nop.Web.csproj`
  and `..\..\Build\ClearPluginAssemblies.proj` references, and
  `PluginPath=$(MSBuildProjectDirectory)\$(OutDir)` for the ClearPluginAssemblies target (4.90 goes
  through `$(SolutionDir)`). `LangVersion` 11, `Nullable` annotations with `string?` on every optional
  model property (with the nullable context on, MVC marks non-nullable strings `[Required]` and nop's
  client validation blocks Save).
- Everything else (settings, controller logic, view component, services, DI startup, model, views,
  locale strings) is the same code; file-scoped namespaces compile fine under C# 11.
- PostgreSQL: 4.60 supports it natively (Npgsql 7.0.0); the compose file uses `postgres:15-alpine`
  and, as on 4.90, lets the wizard create the database so the `citext` extension gets installed.
- The install wizard of 4.60 installs every plugin present in `/Plugins` during setup, so with the
  folder bind-mounted before the wizard runs, the plugin is already installed when you first open
  Local plugins; `verify.mjs` handles both cases.
- 4.60 minifies the storefront HTML (WebMarkupMin is on by default) and drops the optional `</head>`
  end tag. The tag is still emitted byte-exactly, once, inside the head; only tooling that looks for
  `</head>` has to cope (`verify.mjs` stops at `<body` as well).
- The unit tests are C# 11 too (no collection expressions), because the .NET 7 SDK's compiler is
  what runs them for the net7.0 target.

## Adding a nopCommerce version

1. Add the tag, TFM and `sdkImage` to `build/versions.json`.
2. Copy `src/DeploySeal.Nop.Widget.490` (4.80) or `src/DeploySeal.Nop.Widget.460` (4.70) to
   `src/DeploySeal.Nop.Widget.<ver>`, set the TFM and `SupportedVersions`, and fix whatever
   nopCommerce API moved between those versions (compare that version's
   `src/Plugins/Nop.Plugin.Widgets.GoogleAnalytics` with the 4.60 and 4.90 ones: area constant,
   permission check, configuration URL, csproj path style). Do not copy the views: they are linked
   from `src/DeploySeal.Nop.Widget.Shared`.
3. Add `docker/docker-compose.<ver>.yml` (copy 4.60's, change image, port `80<digits>`, volume and
   `name:`), build, `docker compose up -d`, then `node tests/e2e/verify.mjs <ver>`.

## License

MIT.
