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
| 4.60        | release-4.60.6  | net7.0  | `src/DeploySeal.Nop.Widget.460` | planned (first backport)   |
| 4.90        | release-4.90.8  | net9.0  | `src/DeploySeal.Nop.Widget.490` | **reference build, verified end to end** |
| 4.80        | release-4.80.9  | net9.0  | `src/DeploySeal.Nop.Widget.480` | planned                    |
| 4.70        | release-4.70.5  | net8.0  | `src/DeploySeal.Nop.Widget.470` | planned                    |

`build/versions.json` is the single list of versions the build knows about.

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
src/DeploySeal.Nop.Widget.490/    the 4.90 plugin: plugin class, settings, admin controller, view component,
                                  views, route + DI startup, plugin.json, logo.png
tests/DeploySeal.Nop.Core.Tests/  xunit tests for Core (slug, origins, marker composition/limits, byte-exact tag)
tests/e2e/verify.mjs              Playwright script that drives a real nopCommerce in Docker end to end
build/build.ps1                   clone nop tag → copy project in → build → verify folder → zip
build/versions.json               nop version → tag/TFM
build/gen-logo.py                 regenerates logo.png from the product's seal outlines (Pillow only)
docker/docker-compose.4.90.yml    nopcommerceteam/nopcommerce:4.90.8 + PostgreSQL, plugin folder bind-mounted
```

The Core sources are compiled *into* the plugin assembly (`<Compile Include>` in the plugin csproj),
so a store ships exactly one DLL and each nopCommerce version gets a plugin built against its own
TFM. Core is kept to C# 11 / net7.0 APIs so the 4.60 port compiles the same files unchanged.

## Build

Prerequisites: .NET SDK 9 (plus the targeting pack for older TFMs, restored automatically), git,
PowerShell, Docker for the end-to-end check.

```powershell
.\build\build.ps1 -Version 4.90
```

This clones `release-4.90.8` into `.nop/4.90` (depth 1, first time only; nopCommerce assemblies are
not on NuGet so the plugin must be built inside a checkout), copies
`src/DeploySeal.Nop.Widget.490` to `.nop/4.90/src/Plugins/Nop.Plugin.Widgets.DeploySeal`, builds it
with `SolutionDir` set (this also builds Nop.Web; budget several minutes the first time), checks the
output folder holds one DLL + plugin.json + Views + logo, and zips it to
`artifacts/DeploySeal.Nop.Widget-4.90.zip`. It fails clearly for versions that have a
`versions.json` entry but no project yet.

Unit tests:

```powershell
dotnet test tests/DeploySeal.Nop.Core.Tests
```

End to end (real nopCommerce 4.90.8 in Docker, PostgreSQL, install wizard, plugin install,
configure, storefront/admin assertions, screenshot to `artifacts/configure-4.90.png`):

```powershell
.\build\build.ps1 -Version 4.90
docker compose -f docker/docker-compose.4.90.yml up -d
$env:DS_E2E_NODE_MODULES = "<a node_modules folder that has playwright>"   # or npm i playwright in tests/e2e
$env:DS_NOP_CONTAINER = "docker-nop-1"                                      # optional: also proves the SHA-file marker
node tests/e2e/verify.mjs
docker compose -f docker/docker-compose.4.90.yml down -v
```

## Adding a nopCommerce version

1. Add the tag/TFM to `build/versions.json`.
2. Copy `src/DeploySeal.Nop.Widget.490` to `src/DeploySeal.Nop.Widget.<ver>`, set the TFM,
   `SupportedVersions` and whatever nopCommerce API moved (see the port notes in the commit history:
   4.60 uses `AreaNames.Admin`, `IWebHelper.GetStoreLocation()` for the configuration URL,
   `IPermissionService.AuthorizeAsync(StandardPermissionProvider.ManageWidgets)` instead of
   `[CheckPermission]`, relative `..\..\Presentation` paths in the csproj, and block-scoped namespaces).
3. Add `docker/docker-compose.<ver>.yml` and run the same `verify.mjs` with `DS_NOP_VERSION` set.

## License

MIT.
