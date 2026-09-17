// End-to-end proof against a real nopCommerce in Docker (see docker/docker-compose.<ver>.yml):
//
//   1. waits for nopCommerce, completes the install wizard (PostgreSQL) if it is showing,
//   2. logs in as the admin it created,
//   3. makes sure "DeploySeal widget" is installed (Local plugins → Install → Apply changes),
//   4. configures it (fake site key, label "staging"), saves, screenshots the configure page,
//   5. optionally writes a SHA file into the container and checks the "+sha" marker,
//   6. fetches the storefront home page and asserts the EXACT contract tag sits in <head>,
//      and that it is absent on /Admin.
//
// Usage (Playwright is resolved from DS_E2E_NODE_MODULES when this repo has no node_modules):
//   DS_E2E_NODE_MODULES=<path to a node_modules with playwright> node tests/e2e/verify.mjs [4.60|4.70|4.80|4.90]
// The optional argument is the nopCommerce major.minor from build/versions.json; it sets the
// expected full version (from the tag), the port (80 + minor: 4.60 -> 8060, 4.70 -> 8070, 4.80 -> 8080, 4.90 -> 8090) and the
// container name docker/docker-compose.<ver>.yml produces. Env overrides any of them:
//   DS_NOP_VERSION    major.minor or full version (default: the argument, else 4.90)
//   DS_NOP_URL        base URL            (default http://localhost:80<minor>, e.g. 8060)
//   DS_NOP_CONTAINER  docker container name for the SHA-file scenario (default deployseal-nop<digits>-nop-1;
//                     set to "" to skip that scenario)
//   DS_ADMIN_EMAIL / DS_ADMIN_PASSWORD   admin credentials (created by the wizard if needed)
//   DS_SCREENSHOT     output PNG          (default artifacts/configure-<major.minor>.png)
//
// nopCommerce 4.60, 4.70, 4.80 and 4.90 share every selector this script touches (install wizard
// ids, the storefront login button, the Local plugins grid and its install-plugin-link-<SystemName> /
// plugin-apply-changes buttons, the plugin's own Configure page), so there is one script. Nothing
// here is per version: the argument is looked up in build/versions.json, so a new entry there plus a
// docker/docker-compose.<ver>.yml with the matching port and project name is all a new version needs.

import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const modulesDir = process.env.DS_E2E_NODE_MODULES;
const require = createRequire(modulesDir ? path.join(path.resolve(modulesDir), 'x.js') : import.meta.url);
function loadPlaywright() {
  try { return require('playwright'); } catch {}
  // pnpm layouts hoist nothing: go through @playwright/test's real folder, next to which playwright sits.
  const testPkg = require.resolve('@playwright/test/package.json');
  return createRequire(fs.realpathSync(testPkg))('playwright');
}
const { chromium } = loadPlaywright();

// Which nopCommerce: argument or DS_NOP_VERSION, resolved through build/versions.json.
const versions = JSON.parse(fs.readFileSync(path.join(repoRoot, 'build', 'versions.json'), 'utf8'));
const requested = process.env.DS_NOP_VERSION || process.argv[2] || '4.90';
const MAJOR_MINOR = requested.split('.').slice(0, 2).join('.');
const entry = versions[MAJOR_MINOR];
if (!entry) throw new Error(`Unknown nopCommerce version "${requested}"; build/versions.json knows: ${Object.keys(versions).join(', ')}`);
const NOP_VERSION = requested.split('.').length >= 3 ? requested : entry.tag.replace(/^release-/, '');
const DIGITS = MAJOR_MINOR.replace('.', '');
const PORT = '80' + MAJOR_MINOR.split('.')[1]; // 4.60 -> 8060, 4.90 -> 8090
const BASE = (process.env.DS_NOP_URL || `http://localhost:${PORT}`).replace(/\/$/, '');
const CONTAINER = process.env.DS_NOP_CONTAINER !== undefined ? process.env.DS_NOP_CONTAINER : `deployseal-nop${DIGITS}-nop-1`;
const ADMIN_EMAIL = process.env.DS_ADMIN_EMAIL || 'admin@deployseal.test';
const ADMIN_PASSWORD = process.env.DS_ADMIN_PASSWORD || 'Admin!Pass123';
const SCREENSHOT = process.env.DS_SCREENSHOT || path.join(repoRoot, 'artifacts', `configure-${MAJOR_MINOR}.png`);

const SITE_KEY = 'ls_test0000000000000000000000000000000';
const LABEL = 'staging';
const SHA = 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678';
const SHA_FILE = 'App_Data/build-sha.txt';

const tag = (build) =>
  `<script src="https://cdn.deployseal.com/ds-widget.js" data-ds-site-key="${SITE_KEY}" data-ds-environment="${LABEL}"` +
  (build ? ` data-ds-build="${build}"` : '') + ` async></script>`;

const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function assert(cond, msg) { if (!cond) throw new Error('ASSERTION FAILED: ' + msg); }

async function waitForServer(label, predicate, timeoutMs = 6 * 60 * 1000) {
  const start = Date.now();
  let last = '';
  while (Date.now() - start < timeoutMs) {
    try {
      const res = await fetch(BASE + '/', { redirect: 'manual' });
      const body = res.status >= 200 && res.status < 400 && res.status !== 301 && res.status !== 302 ? await res.text() : '';
      last = `${res.status} ${res.headers.get('location') || ''}`;
      if (await predicate(res, body)) { log(`${label}: ready (${last})`); return; }
    } catch (e) { last = e.message; }
    await sleep(3000);
  }
  throw new Error(`${label}: timed out (${last})`);
}

const isInstalled = (res, body) => res.status === 200 && !/id="installation-form"/.test(body) && !/\/install/i.test(res.headers.get('location') || '');
const isUp = () => true;

async function fetchHtml(context, url) {
  const res = await context.request.get(url, { maxRedirects: 5 });
  return { status: res.status(), url: res.url(), html: await res.text() };
}

async function main() {
  fs.mkdirSync(path.dirname(SCREENSHOT), { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1400, height: 1000 }, ignoreHTTPSErrors: true });
  const page = await context.newPage();
  page.setDefaultTimeout(90_000);
  const summary = { base: BASE, nopVersion: NOP_VERSION, steps: [] };

  try {
    // 1. Install wizard --------------------------------------------------------------------------
    await waitForServer('nopCommerce responding', isUp);
    await page.goto(BASE + '/');
    if (await page.locator('#installation-form').count()) {
      log('install wizard showing; filling it (PostgreSQL @ db)');
      await page.fill('#AdminEmail', ADMIN_EMAIL);
      await page.fill('#AdminPassword', ADMIN_PASSWORD);
      await page.fill('#ConfirmPassword', ADMIN_PASSWORD);
      await page.selectOption('#DataProvider', 'PostgreSQL');
      await page.fill('#ServerName', 'db');
      await page.fill('#DatabaseName', 'nop');
      await page.fill('#Username', 'nop');
      await page.fill('#Password', 'nop');
      await page.check('#CreateDatabaseIfNotExists');
      if (await page.locator('#InstallSampleData').count()) await page.uncheck('#InstallSampleData');
      if (await page.locator('#SubscribeNewsletters').count()) await page.uncheck('#SubscribeNewsletters');
      await page.click('#installation-form button.btn-install');
      // The wizard POST takes a while (schema + data). It answers with the same page + a restart script,
      // or with .message-error on failure.
      await page.waitForLoadState('load', { timeout: 15 * 60 * 1000 });
      const errText = (await page.locator('.message-error').allTextContents()).join(' ').trim();
      if (errText) throw new Error('install wizard reported: ' + errText);
      log('wizard accepted; waiting for nopCommerce to restart');
      await sleep(10_000);
      await waitForServer('nopCommerce installed', isInstalled, 10 * 60 * 1000);
      summary.steps.push('install wizard completed (PostgreSQL)');
    } else {
      await waitForServer('nopCommerce installed', isInstalled, 60_000);
      summary.steps.push('nopCommerce was already installed');
    }

    // 2. Admin login -----------------------------------------------------------------------------
    await page.goto(BASE + '/login');
    await page.fill('#Email', ADMIN_EMAIL);
    await page.fill('#Password', ADMIN_PASSWORD);
    await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button.login-button')]);
    await page.goto(BASE + '/Admin');
    assert(/\/Admin/i.test(page.url()) && !/\/login/i.test(page.url()), 'admin login failed: ' + page.url());
    summary.steps.push('admin login ok');

    // 3. Plugin installed? -----------------------------------------------------------------------
    await page.goto(BASE + '/Admin/Plugin/List');
    await page.fill('#SearchFriendlyName', 'DeploySeal');
    await page.click('#search-plugins-local');
    await page.waitForSelector('#plugins-local-grid tbody tr', { timeout: 60_000 });
    await sleep(1500);
    const rowText = await page.locator('#plugins-local-grid tbody').innerText();
    assert(/DeploySeal widget/.test(rowText), 'DeploySeal widget is not listed under Local plugins (is the plugin folder mounted?)');
    const installBtn = page.locator('button[name="install-plugin-link-Widgets.DeploySeal"]');
    if (await installBtn.count()) {
      log('plugin not installed yet; installing');
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), installBtn.click()]);
      await page.click('button[name="plugin-apply-changes"]');
      await sleep(10_000);
      await waitForServer('nopCommerce restarted after plugin install', isInstalled, 10 * 60 * 1000);
      summary.steps.push('plugin installed via Local plugins + Apply changes');
      await page.goto(BASE + '/login');
      if (await page.locator('#Email').count()) {
        await page.fill('#Email', ADMIN_EMAIL);
        await page.fill('#Password', ADMIN_PASSWORD);
        await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button.login-button')]);
      }
    } else {
      assert(await page.locator('button[name="uninstall-plugin-link-Widgets.DeploySeal"]').count(), 'neither Install nor Uninstall button found for the plugin');
      summary.steps.push('plugin was already installed (nopCommerce installs every present plugin during setup)');
    }

    // 4. Configure -------------------------------------------------------------------------------
    await page.goto(BASE + '/Admin/WidgetsDeploySeal/Configure');
    await page.waitForSelector('#SiteKey');
    const derived = (await page.locator('#deployseal-environment').innerText()).trim();
    log('label before configuration (derived at install):', JSON.stringify(derived));
    await page.fill('#SiteKey', SITE_KEY);
    await page.fill('#EnvironmentLabel', LABEL);
    await page.check('#Enabled');
    await page.fill('#GitShaFilePath', '');
    await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="save"]')]);
    await page.waitForSelector('#deployseal-snippet');
    const preview = (await page.locator('#deployseal-snippet').innerText()).trim();
    const shownBuild = (await page.locator('#deployseal-build').innerText()).trim();
    const shownOrigins = await page.locator('code.deployseal-origin').allTextContents();
    log('configure page shows build marker', JSON.stringify(shownBuild), 'origins', JSON.stringify(shownOrigins));
    assert(preview === tag(NOP_VERSION), `configure preview mismatch:\n  got  ${preview}\n  want ${tag(NOP_VERSION)}`);
    assert(shownBuild === NOP_VERSION, `configure page build marker: got ${shownBuild}, want ${NOP_VERSION}`);
    const expectedOrigin = new URL(BASE).origin;
    assert(shownOrigins.includes(expectedOrigin), `configure page origins ${JSON.stringify(shownOrigins)} do not include ${expectedOrigin}`);
    // nop's sidebar is position:fixed and would be painted over the left column of a full-page shot.
    await page.addStyleTag({ content: '.main-sidebar{display:none!important} .content-wrapper,.main-header,.main-footer{margin-left:0!important}' });
    await page.screenshot({ path: SCREENSHOT, fullPage: true });
    summary.steps.push(`configured (key, label "${LABEL}", enabled); screenshot ${SCREENSHOT}`);
    summary.configurePreview = preview;
    summary.origins = shownOrigins;

    // 5. Storefront + admin assertions (fresh, anonymous context) --------------------------------
    const anon = await browser.newContext();
    const home = await fetchHtml(anon, BASE + '/');
    assert(home.status === 200, 'storefront home returned ' + home.status);
    // nopCommerce 4.60 minifies its HTML (WebMarkupMin) and drops the optional </head> end tag, so
    // the head runs from <head> to whichever of </head> or <body comes first.
    const head = home.html.match(/<head[\s\S]*?(?=<\/head>|<body[\s>])/i)?.[0] || '';
    assert(head.length > 0, 'could not find <head> in the storefront HTML');
    const expected = tag(NOP_VERSION);
    assert(head.includes(expected), `storefront <head> does not contain the exact tag.\n  want ${expected}\n  head snippet: ${(home.html.match(/<script[^>]*ds-widget[^>]*>/i) || ['<none>'])[0]}`);
    assert(home.html.split('ds-widget.js').length === 2, 'the tag must appear exactly once');
    summary.storefrontTag = expected;
    log('storefront emitted:', expected);
    await anon.close();

    const admin = await fetchHtml(context, BASE + '/Admin');
    assert(admin.status === 200 && /\/Admin/i.test(admin.url), 'could not load /Admin as admin: ' + admin.status + ' ' + admin.url);
    assert(!admin.html.includes('ds-widget.js'), '/Admin must not contain the widget tag');
    summary.steps.push('storefront <head> has the exact tag once; /Admin has none');

    // 6. SHA file scenario (optional) --------------------------------------------------------------
    if (CONTAINER) {
      log(`writing ${SHA_FILE} into container ${CONTAINER}`);
      execFileSync('docker', ['exec', CONTAINER, 'sh', '-c', `printf '%s\\n' '${SHA}' > /app/${SHA_FILE}`], { stdio: 'inherit' });
      await page.goto(BASE + '/Admin/WidgetsDeploySeal/Configure');
      await page.fill('#GitShaFilePath', SHA_FILE);
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="save"]')]);
      const shaBuild = (await page.locator('#deployseal-build').innerText()).trim();
      assert(shaBuild === `${NOP_VERSION}+${SHA.slice(0, 7)}`, `with SHA file: got ${shaBuild}`);
      const anon2 = await browser.newContext();
      const home2 = await fetchHtml(anon2, BASE + '/');
      assert(home2.html.includes(tag(`${NOP_VERSION}+${SHA.slice(0, 7)}`)), 'storefront did not emit the +sha marker');
      await anon2.close();
      summary.storefrontTagWithSha = tag(`${NOP_VERSION}+${SHA.slice(0, 7)}`);
      log('storefront emitted (with SHA file):', summary.storefrontTagWithSha);
      // Put it back so the screenshot/state matches the default source description.
      await page.fill('#GitShaFilePath', '');
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="save"]')]);
      summary.steps.push('git SHA file scenario ok (version+sha7 emitted)');
    }

    console.log('\nVERIFIED\n' + JSON.stringify(summary, null, 2));
  } catch (e) {
    try { await page.screenshot({ path: path.join(path.dirname(SCREENSHOT), `failure-${MAJOR_MINOR}.png`), fullPage: true }); } catch {}
    console.error('\nFAILED:', e.message, '\nat', page.url());
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main();
