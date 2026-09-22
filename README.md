# DeploySeal widget for nopCommerce

A nopCommerce widget plugin that loads the [DeploySeal](https://deployseal.com) tester widget on
the storefront and declares, per store, **which environment** the page belongs to and **which
build** is running, so the readiness report can prove what was tested. It is one of the
DeploySeal install paths and obeys the DeploySeal install contract to the byte: the tag it emits is

```html
<script src="https://cdn.deployseal.com/ds-widget.js" data-ds-site-key="ls_…" data-ds-environment="staging" data-ds-build="4.90.8+a1b2c3d" data-ds-installer="nopcommerce-plugin/1.3.0" async></script>
```

placed in `<head>`, once, only on the public storefront (never in `/Admin` unless you opt in).
`data-ds-installer` (install contract v1.2) names this plugin and its version as the install path,
so DeploySeal's site page says *Installed via the nopCommerce plugin 1.3.0*, opens its install
drawer on the plugin, and warns when a pasted snippet is still live on the same environment —
remove any snippet from your theme or *Custom `<head>` HTML* once the plugin is enabled.

With an organisation API key it also reports the **platform inventory** — every plugin nopCommerce
knows about, with its version and whether it is installed — so the readiness report can print
exactly which plugins were installed when a release was tested (install contract §6).

## Plugin version

| Plugin | Released   | What changed |
|--------|------------|--------------|
| 1.3.0  | 2026-09-22 | Emits `data-ds-installer="nopcommerce-plugin/1.3.0"` (install contract v1.2) so DeploySeal shows the plugin as the install path and can warn about a second, pasted tag. |
| 1.2.0  | 2026-09-19 | See the [release notes](https://github.com/deployseal/nopcommerce-plugin/releases). |
| 1.1.0  | 2026-09-19 | See the [release notes](https://github.com/deployseal/nopcommerce-plugin/releases). |

The version lives in each project's `plugin.json` and `DeploySealDefaults.PluginVersion` (they must
match — the tag's `data-ds-installer` is built from the latter); pushing a `v*` tag releases the five assets.

## Supported nopCommerce versions

| nopCommerce | Tag             | .NET    | Plugin project                  | Status                     |
|-------------|-----------------|---------|---------------------------------|----------------------------|
| 4.60        | release-4.60.6  | net7.0  | `src/DeploySeal.Nop.Widget.460` | **verified end to end incl. inventory** (built in the .NET 7 SDK container) |
| 4.90        | release-4.90.8  | net9.0  | `src/DeploySeal.Nop.Widget.490` | **reference build, verified end to end incl. inventory** |
| 4.80        | release-4.80.9  | net9.0  | `src/DeploySeal.Nop.Widget.480` | **verified end to end incl. inventory** (built with the local SDK 9) |
| 4.70        | release-4.70.5  | net8.0  | `src/DeploySeal.Nop.Widget.470` | **verified end to end incl. inventory** (built in the .NET 8 SDK container) |

`build/versions.json` is the single list of versions the build knows about (tag, TFM and the SDK
container image used when that SDK is not installed locally).

The plugin behaves identically on every supported version: same settings, same Configure page,
same tag. The only visible difference is the build marker's default, which is that version's
`NopVersion.FULL_VERSION` (`4.60.6`, `4.70.5`, `4.80.9`, `4.90.8`).

## Install (store administrator)

1. Download `DeploySeal.Nop.Widget-all-versions.zip` from the [latest release](../../releases/latest)
   — one archive that carries all four supported builds (4.60, 4.70, 4.80, 4.90) behind an
   `uploadedItems.json` manifest, the same multi-version shape the nopCommerce Marketplace uses.
   You do not pick a per-version zip yourself; nopCommerce reads its own version and installs the
   matching build. (The four `DeploySeal.Nop.Widget-<version>.zip` files are still attached to
   every release for a manual, single-version install — see below.)
2. Administration → Configuration → Local plugins → **Upload plugin or theme**, pick
   `DeploySeal.Nop.Widget-all-versions.zip`, then **Install** and **Apply changes** (nopCommerce
   restarts).
3. Open the plugin's **Configure** page. Only two settings are required: **Site key** and
   **Enabled**. Everything else already has a sensible default, so the page reads paste key, save,
   confirm — the **Essentials** card holds just those two plus the environment label (usually fine
   as guessed), with a **Save** button right there. Below it, "What this store will send" confirms
   what you just saved: the exact origins to register, the environment label it will declare, the
   exact build marker it will emit and where that comes from, and the tag it will render.
4. In DeploySeal: create an environment for the store, register the origins shown, copy the
   environment's public key into **Site key**, tick **Enabled**, **Save**.
5. Load the storefront once; the environment shows as *Live* in DeploySeal.

### Manual, single-version install

If you would rather install the zip for your exact nopCommerce version by hand: download
`DeploySeal.Nop.Widget-<version>.zip`, then either upload it the same way through **Upload plugin
or theme**, or unzip it so that `Plugins/Widgets.DeploySeal/plugin.json` exists under your
nopCommerce installation, then **Install** and **Apply changes**. Steps 3–5 above are the same
either way.

## Settings

All settings are overridable per store (multi-store: one key per environment per store). The
Configure page groups them into three cards: **Essentials** (open, the two settings almost every
store needs), **Advanced** and **Platform inventory** (both collapsed by default — folded away
because their defaults are almost always right, not hidden; either one opens itself automatically
the moment something inside it is not the default, so nothing you have already set is ever hidden).

### Essentials

| Setting | Default | Meaning |
|---|---|---|
| Enabled | off | Master switch; nothing renders while off. |
| Site key | empty | The environment's public key (`ls_…`; legacy `ds_…` keys still work). Not a secret: it only works from the environment's registered origins. |
| Environment label | guessed from the store URL: `staging` when the host contains staging/uat/test/dev/qa/sandbox/preprod/localhost, else `production` | Declared label, `[a-z0-9-]`, ≤ 32. Saved slugged; an empty value falls back to the guess — usually fine as guessed. |

### Advanced (collapsed by default)

| Setting | Default | Meaning |
|---|---|---|
| Build marker source | nopCommerce version + git SHA when available | One of: nopCommerce version (`4.90.8`), nopCommerce version + git SHA (`4.90.8+a1b2c3d`, degrades to the version alone when no SHA can be read), git SHA only (emits nothing when unreadable), manual. |
| Git SHA file path | empty | File holding the deployed commit SHA (first line, 7–40 hex chars). Relative paths resolve from the application root, e.g. `App_Data/build-sha.txt`. Have your deploy pipeline write it. When no SHA can be read, "What this store will send" shows a one-line notice ("No SHA — see Advanced"); the full explanation is here, next to this field. |
| Manual build marker | empty | Used by the manual source. One token, no whitespace, ≤ 64. |
| Script host | `https://cdn.deployseal.com` | The only supported host; leave it. |
| Also load in the admin area | off | The admin layout has no head zone, so this uses the first admin body zone. |

### Platform inventory (collapsed by default)

| Setting | Default | Meaning |
|---|---|---|
| DeploySeal API key | empty | An organisation API key with the **Write** scope (DeploySeal → Settings → Integrations → API keys). A secret: the page never shows it again; leaving the box empty on Save keeps it, the "Remove the stored API key" box deletes it. Only used for the inventory. |
| API base | `https://api.deployseal.com` | The API host the inventory is posted to. Self-hosters change it; `verify.mjs` points it at a stub. |
| Send inventory on a schedule | off | Post the inventory every 6 hours through the scheduled task. "Send inventory now" works without it. |

This card also shows the plugin-count/fingerprint/"sent to" facts and the **Send inventory now**
button described below.

The build marker is resolved once per request. Whatever it resolves to is printed on the Configure
page, verbatim, so it can be copied into a campaign's release identifier (`a1b2c3d` does **not**
confirm `4.90.8+a1b2c3d`; prefix matching runs from the start of the string).

## Platform inventory

What is sent, to `POST {API base}/api/v1/sites/{site key}/inventory` with `Authorization: Bearer <API key>`:

```json
{ "platform": "nopcommerce", "platformVersion": "4.90.8", "buildMarker": "4.90.8+a1b2c3d",
  "capturedAt": "2026-09-17T06:00:00.000Z",
  "items": [ { "systemName": "Payments.PayPalCommerce", "name": "PayPal Commerce", "version": "4.90.1", "enabled": true }, … ] }
```

- `items` is every plugin descriptor nopCommerce loads (`IPluginService.GetPluginDescriptorsAsync(LoadPluginsMode.All)`):
  installed **and** not installed, so a disabled plugin is on the record as `enabled: false`. Sorted by
  system name, de-duplicated, capped at 500 items and the contract's field lengths (128 / 200 / 32).
  Nothing else leaves the store: no settings, no customers, no orders.
- `platformVersion` is `NopVersion.FULL_VERSION`; `buildMarker` is the same marker the storefront tag
  carries (omitted as `null` when none is emitted).
- **When:** the **Send inventory now** button on the Configure page (result shown on the page: `HTTP 201`
  with the count and snapshot id, `HTTP 200 … already on record` when nothing changed, or the API's error),
  and the scheduled task **Send platform inventory to DeploySeal** (Administration → System → Schedule
  tasks; every 6 hours; only stores whose settings have *Send inventory on a schedule* on plus a site key
  and an API key; stores sharing one key are sent once). Resending an unchanged list does not grow the
  record — the API is idempotent on (environment, item set, capturedAt).
- **Which key:** an organisation API key with the **Write** scope. `Read` alone is refused (403); a key of
  another organisation, or a site key that is not one of that organisation's environments, is a plain 404.
- The call is server to server with a 15 s timeout, one retry on a transport failure, and never throws
  into the page or the task runner; failures are logged under Administration → System → Log.
- The Configure page shows how many plugins the next send would carry, the canonical fingerprint
  (`sha256:…`, the same value the readiness report prints) and the exact URL.

## Repository layout

```
src/DeploySeal.Nop.Core/          pure logic, no nopCommerce references, multi-targets net7.0;net9.0, C# 11:
                                  settings POCO, EnvironmentLabel (slug + guess), OriginFormatter,
                                  GitSha, BuildMarkerResolver, SnippetBuilder, contract constants,
                                  Inventory/ (InventoryItem, InventorySnapshotBuilder = canonical JSON +
                                  sha256 + request body, InventoryClient = the HTTP send)
src/DeploySeal.Nop.Widget.Shared/ what is byte-identical across nopCommerce versions and linked into every
                                  plugin project: the three Razor views (Configure, PublicInfo, _ViewImports)
                                  and logo.png
src/DeploySeal.Nop.Widget.490/    the 4.90 plugin: plugin class, settings, admin controller, view component,
                                  route + DI startup, plugin.json, Services/InventoryService (plugin list →
                                  snapshot → send) + InventorySyncTask (the schedule task)
src/DeploySeal.Nop.Widget.480/    the 4.80 plugin (net9.0): the 4.90 files with one API difference (see
                                  "nopCommerce 4.80 and 4.70 notes")
src/DeploySeal.Nop.Widget.470/    the 4.70 plugin (net8.0): the 4.90 files with the 4.80 URL difference, the
                                  4.60 permission check and the 4.60 csproj path style
src/DeploySeal.Nop.Widget.460/    the 4.60 plugin (net7.0): same files minus the RouteProvider, with the
                                  4.60 API spellings (see "nopCommerce 4.60 notes")
tests/DeploySeal.Nop.Core.Tests/  xunit tests for Core (slug, origins, marker composition/limits, byte-exact tag,
                                  inventory canonical form / limits / request body / client behaviour)
tests/e2e/verify.mjs              Playwright script that drives a real nopCommerce in Docker end to end, with a
                                  stub DeploySeal API on the host for the inventory request shape
tests/e2e/verify-upload.mjs       Playwright script that proves the all-versions bundle through nopCommerce's
                                  real "Upload plugin or theme" path (no plugin folder mounted)
build/build.ps1                   clone nop tag → copy project in → build (locally or in the SDK container) → verify folder → zip
build/bundle.ps1                  packs the four built zips into artifacts/DeploySeal.Nop.Widget-all-versions.zip
                                  (uploadedItems.json manifest, one nopCommerce Marketplace upload for every version)
build/versions.json               nop version → tag / TFM / SDK container image
build/gen-logo.py                 regenerates logo.png from the product's seal outlines (Pillow only)
docker/docker-compose.4.60.yml    nopcommerceteam/nopcommerce:4.60.6 + PostgreSQL 15, port 8060, plugin folder bind-mounted
docker/docker-compose.4.70.yml    nopcommerceteam/nopcommerce:4.70.5 + PostgreSQL 16, port 8070, plugin folder bind-mounted
docker/docker-compose.4.80.yml    nopcommerceteam/nopcommerce:4.80.9 + PostgreSQL 17, port 8080, plugin folder bind-mounted
docker/docker-compose.4.90.yml    nopcommerceteam/nopcommerce:4.90.8 + PostgreSQL 17, port 8090, plugin folder bind-mounted
```

The Core sources are compiled *into* the plugin assembly (`<Compile Include>` in the plugin csproj),
so a store ships exactly one DLL and each nopCommerce version gets a plugin built against its own
TFM. Core is kept to C# 11 / net7.0 APIs so the 4.60 port compiles the same files unchanged. The
views and the logo are linked from `src/DeploySeal.Nop.Widget.Shared` the same way (`<Content
Link>`), so there is one copy of the Configure page; the per-version folders hold only C# and
`plugin.json`. The four supported versions all render that one copy: the tag helpers it uses
(`nop-editor`, `nop-select`, `nop-label`, `nop-override-store-checkbox`), the `_ConfigurePlugin`
layout and `StoreScopeConfigurationViewComponent` have the same names and attributes from 4.60 to
4.90, so no per-version view copy was needed.

## Build

Prerequisites: git, PowerShell, Docker (for the end-to-end check and for any version whose .NET SDK
is not installed), and the .NET SDK matching the version's TFM for a native build (SDK 9 for 4.90 and
4.80). No SDK has to be installed for the older versions: see "Building in the SDK container".

```powershell
.\build\build.ps1 -Version 4.90
.\build\build.ps1 -Version 4.80
.\build\build.ps1 -Version 4.70
.\build\build.ps1 -Version 4.60
```

This clones the version's tag into `.nop/<ver>` (depth 1, first time only; nopCommerce assemblies are
not on NuGet so the plugin must be built inside a checkout), copies
`src/DeploySeal.Nop.Widget.<ver>` to `.nop/<ver>/src/Plugins/Nop.Plugin.Widgets.DeploySeal`, builds it
(this also builds Nop.Web; budget several minutes the first time), checks the output folder holds one
DLL + plugin.json + Views + logo, and zips it to `artifacts/DeploySeal.Nop.Widget-<ver>.zip`. It fails
clearly for versions that have a `versions.json` entry but no project yet.

### Building in the SDK container

nopCommerce 4.60 is net7.0 (SDK end of life) and 4.70 is net8.0 (LTS until November 2026, but not
what a machine that builds 4.80/4.90 has installed); neither SDK is expected on a developer machine.
When `dotnet --list-sdks` shows no SDK with the TFM's major version (or when `-UseDocker` is passed),
`build.ps1` runs **only the build step** inside the version's `sdkImage` from `versions.json`
(`mcr.microsoft.com/dotnet/sdk:7.0` for 4.60, `sdk:8.0` for 4.70) with the repository mounted at
`/work` and a named volume (`deployseal-nuget`) caching NuGet packages between runs. Clone, copy,
folder verification and zip stay on the host, and the plugin folder lands in the same place
(`.nop/<ver>/src/Presentation/Nop.Web/Plugins/Widgets.DeploySeal`), so the compose files and
`verify.mjs` do not care which way it was built. `obj/` and `bin/` are written into the bind-mounted
checkout, so a later host build of the same version (should you install that SDK) starts from a
Linux-produced `obj/`; delete `.nop/<ver>/src/**/obj` first in that case. Measured on Windows with
Docker Desktop (bind mount, cold NuGet cache): 4 min 30 s for the first 4.60 build (restore of the
Nop.Web dependency tree took 1 min 15 s of that; the rest is compiling Nop.Web and its libraries),
2 min 5 s for the 4.70 build step once the net8.0 packages were in the volume. The script prints
"Build step took N s" so the first run is not mistaken for a hang.

Unit tests (Core multi-targets net7.0 and net9.0; the test project runs on net9.0 by default and on
whatever `DeploySealTestTfm` names):

```powershell
dotnet test tests/DeploySeal.Nop.Core.Tests
# the net7.0 build of Core, in the same container the 4.60 plugin is built in:
docker run --rm -v "${PWD}:/work" -v deployseal-nuget:/root/.nuget/packages -w /work mcr.microsoft.com/dotnet/sdk:7.0 `
  dotnet test tests/DeploySeal.Nop.Core.Tests -p:DeploySealTestTfm=net7.0
```

End to end (real nopCommerce in Docker, PostgreSQL, install wizard, plugin install, configure,
storefront/admin assertions, SHA-file marker, inventory request shape against a stub API on the host
(`DS_STUB_PORT`, default `180<minor>`; the container reaches it as `host.docker.internal`, override
with `DS_STUB_HOST`), scheduled task registered, screenshot to `artifacts/configure-<ver>.png`):

```powershell
.\build\build.ps1 -Version 4.60                                             # or 4.70 / 4.80 / 4.90
docker compose -f docker/docker-compose.4.60.yml up -d                      # port 8060 (4.70: 8070, 4.80: 8080, 4.90: 8090)
$env:DS_E2E_NODE_MODULES = "<a node_modules folder that has playwright>"   # or npm i playwright in tests/e2e
node tests/e2e/verify.mjs 4.60
docker compose -f docker/docker-compose.4.60.yml down -v
```

### The all-versions bundle

`build/bundle.ps1` packs the four zips `build/build.ps1` produces into
`artifacts/DeploySeal.Nop.Widget-all-versions.zip` — a single archive with an `uploadedItems.json`
manifest at its root (one entry per nopCommerce version, each pointing at that version's copy of
the plugin folder inside the archive), which is the shape nopCommerce's own
`Nop.Services.Plugins.UploadService` expects for a multi-version upload:

```powershell
.\build\build.ps1 -Version 4.60   # repeat for 4.70 / 4.80 / 4.90, or use the four zips already in artifacts/
.\build\bundle.ps1                # -> artifacts/DeploySeal.Nop.Widget-all-versions.zip
```

To prove it through the real Marketplace upload path (not the bind-mount `docker-compose.<ver>.yml`
files use) against a container with **no** plugin folder mounted:

```powershell
docker compose -f docker/docker-compose.4.90.no-mount.yml up -d      # port 8091 (4.60: 8061)
node tests/e2e/verify-upload.mjs 4.90
docker compose -f docker/docker-compose.4.90.no-mount.yml down -v
```

`verify.mjs <ver>` takes the expected full version from the tag in `versions.json`, the port from the
version digits (`80` + `60`) and the container name from the compose project name
(`deployseal-nop460-nop-1`), all overridable with `DS_NOP_VERSION`, `DS_NOP_URL`, `DS_NOP_CONTAINER`
(empty skips the SHA-file scenario). The same script drives all four versions: every selector it
touches (install wizard, storefront login, Local plugins grid, Apply changes, the plugin's Configure
page) is the same in each.

## nopCommerce 4.80 and 4.70 notes

Both projects are the 4.90 files with the smallest set of edits their API surface forces. What
moved between the versions, from newest to oldest (compare each tag's
`src/Plugins/Nop.Plugin.Widgets.GoogleAnalytics` with its neighbours):

| | 4.90 | 4.80 | 4.70 | 4.60 |
|---|---|---|---|---|
| Configuration URL | `INopUrlHelper.RouteUrl(routeName)` | `IUrlHelperFactory.GetUrlHelper(IActionContextAccessor.ActionContext).RouteUrl(routeName)` | same as 4.80 | `IWebHelper.GetStoreLocation()` + path |
| Named route (`RouteProvider`) | yes | yes | yes | no |
| Permission check | `[CheckPermission(StandardPermission.Configuration.MANAGE_WIDGETS)]` | same as 4.90 | inline `IPermissionService.AuthorizeAsync(StandardPermissionProvider.ManageWidgets)` | same as 4.70 |
| Inventory (`IPluginService.GetPluginDescriptorsAsync`, `IScheduleTask`, `IScheduleTaskService`, `AddHttpClient<T>().WithProxy()`) | identical | identical | identical | identical |
| Area constant | `AreaNames.ADMIN` | same | same | `AreaNames.Admin` |
| csproj paths | `$(SolutionDir)` | same | relative `..\..\` + `PluginPath=$(MSBuildProjectDirectory)\$(OutDir)` | same as 4.70 |
| TFM / C# | net9.0 / latest | net9.0 / latest | net8.0 / 12 | net7.0 / 11 |
| PostgreSQL in Docker | 17 (Npgsql 9.0.1) | 17 (Npgsql 9.0.1) | 16 (Npgsql 8.0.2) | 15 (Npgsql 7.0.0) |

- **4.80** differs from 4.90 in exactly one method: `INopUrlHelper` exists but gains `RouteUrl` only in
  4.90, so `GetConfigurationPageUrl()` resolves the named route through MVC's own `IUrlHelper`
  (`IUrlHelperFactory` + `IActionContextAccessor`, both registered by nopCommerce), which is what the
  4.80 Google Analytics plugin does. `RouteUrl(string)` is an extension method in
  `Microsoft.AspNetCore.Mvc`, hence that `using`. Everything else (csproj shape, `[CheckPermission]`,
  `AreaNames.ADMIN`, `RouteProvider`) is the 4.90 code unchanged. Built with the local SDK 9 (the
  checkout's `global.json` pins 9.0.100 with `latestFeature` roll-forward).
- **4.70** is a hybrid: it already has `AreaNames.ADMIN`, file-scoped namespaces and the plugin
  `RouteProvider` (so it starts from 4.90, not 4.60), takes the 4.80 configuration-URL code, but has
  no `[CheckPermission]` yet (4.80 introduced it) and its own plugins still reference Nop.Web and
  `Build/ClearPluginAssemblies.proj` relatively, so the controller and csproj follow the 4.60 project.
  `LangVersion` 12 (net8.0's default); the shared code is C# 11 either way. Built inside
  `mcr.microsoft.com/dotnet/sdk:8.0`.
- Like 4.60, the 4.70 **and** 4.80 install wizards install every plugin present in `/Plugins`, so with
  the folder bind-mounted before the wizard runs the plugin is already installed when Local plugins is
  first opened (`verify.mjs` reports "plugin was already installed").
- The storefront tag is byte-identical to 4.90's apart from the default build marker:
  `data-ds-build="4.80.9"` and `data-ds-build="4.70.5"` (`…+a1b2c3d` with a SHA file).

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
- The inventory feature needed no per-version change at all: `IPluginService.GetPluginDescriptorsAsync<IPlugin>(LoadPluginsMode.All)`,
  `PluginDescriptor.{SystemName, FriendlyName, Version, Installed}`, `IScheduleTask`, `IScheduleTaskService`
  and the `WithProxy()` HttpClient extension have the same names and signatures from 4.60 to 4.90. Only the
  `SendInventory` controller action follows each version's permission style (attribute vs inline check).
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
2. Copy the nearest existing project (`src/DeploySeal.Nop.Widget.490` for anything newer than
   4.90, the matching neighbour for anything in between) to `src/DeploySeal.Nop.Widget.<ver>`, set
   the TFM and `SupportedVersions`, and fix whatever nopCommerce API moved (compare that version's
   `src/Plugins/Nop.Plugin.Widgets.GoogleAnalytics` with its neighbours: area constant, permission
   check, configuration URL, csproj path style; the table above lists what moved so far). Do not copy
   the views: they are linked from `src/DeploySeal.Nop.Widget.Shared`; check that version's
   `Nop.Web.Framework/TagHelpers/Admin` still has the same tag helpers and attributes first.
3. Add `docker/docker-compose.<ver>.yml` (copy 4.60's, change image, port `80<digits>`, volume and
   `name:`), build, `docker compose up -d`, then `node tests/e2e/verify.mjs <ver>`.

## License

MIT.
