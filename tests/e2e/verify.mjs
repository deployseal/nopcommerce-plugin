// End-to-end proof against a real nopCommerce in Docker (see docker/docker-compose.<ver>.yml):
//
//   1. waits for nopCommerce, completes the install wizard (PostgreSQL) if it is showing,
//   2. logs in as the admin it created,
//   3. makes sure "DeploySeal widget" is installed (Local plugins → Install → Apply changes),
//   4. configures it (fake site key, label "staging"), saves, screenshots the configure page,
//   5. optionally writes a SHA file into the container and checks the "+sha" marker,
//   6. fetches the storefront home page and asserts the EXACT contract tag sits in <head>,
//      and that it is absent on /Admin,
//   7. starts a stub DeploySeal API on the host, points the plugin's "API base" at it through
//      host.docker.internal, presses "Send inventory now" and asserts the request shape
//      (POST /api/v1/sites/{key}/inventory, Bearer key, JSON body with platform/version/build/
//      capturedAt/items ≥ 1, canonical order), the 201/200 answers surfacing on the page, and
//      the scheduled task being registered.
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
//   DS_STUB_PORT      host port of the stub API the container posts the inventory to (default 180<minor>, e.g. 18090)
//   DS_STUB_HOST      how the container reaches the host (default host.docker.internal; Docker Desktop resolves it)
//
// nopCommerce 4.60, 4.70, 4.80 and 4.90 share every selector this script touches (install wizard
// ids, the storefront login button, the Local plugins grid and its install-plugin-link-<SystemName> /
// plugin-apply-changes buttons, the plugin's own Configure page), so there is one script. Nothing
// here is per version: the argument is looked up in build/versions.json, so a new entry there plus a
// docker/docker-compose.<ver>.yml with the matching port and project name is all a new version needs.

import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import http from 'node:http';
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
const API_KEY = 'ds_test_e2e0000000000000000000000000000';
const STUB_PORT = Number(process.env.DS_STUB_PORT || ('180' + MAJOR_MINOR.split('.')[1]));
const STUB_HOST = process.env.DS_STUB_HOST || 'host.docker.internal';
const STUB_BASE = `http://${STUB_HOST}:${STUB_PORT}`;
const LABEL = 'staging';
const SHA = 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678';
const SHA_FILE = 'App_Data/build-sha.txt';

const tag = (build) =>
  `<script src="https://cdn.deployseal.com/ds-widget.js" data-ds-site-key="${SITE_KEY}" data-ds-environment="${LABEL}"` +
  (build ? ` data-ds-build="${build}"` : '') + ` async></script>`;

const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function assert(cond, msg) { if (!cond) throw new Error('ASSERTION FAILED: ' + msg); }

// The Configure page folds "Advanced" and "Platform inventory" into collapsed <details> cards when
// their settings are all still default, so their fields are not visible (and Playwright's
// actionability checks would time out) until opened. The page opens them itself once something
// inside is non-default; on a still-default load this forces them open before touching a field.
async function expandDetails(page, id) {
  await page.evaluate((elId) => {
    const el = document.getElementById(elId);
    if (el && !el.open) el.open = true;
  }, id);
}

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

// A stand-in for api.deployseal.com: records every request and answers like the real endpoint
// (201 for a new item set, 200 with the same id when the identical set is posted again).
function startStub() {
  const requests = [];
  const seen = new Map();
  const server = http.createServer((req, res) => {
    let raw = '';
    req.on('data', (c) => { raw += c; });
    req.on('end', () => {
      let body = null;
      try { body = JSON.parse(raw); } catch {}
      requests.push({ method: req.method, url: req.url, headers: req.headers, raw, body });
      const items = body && Array.isArray(body.items) ? body.items : null;
      if (req.method !== 'POST' || !/^\/api\/v1\/sites\/[^/]+\/inventory$/.test(req.url) || !items) {
        res.writeHead(400, { 'content-type': 'application/problem+json' });
        res.end(JSON.stringify({ status: 400, title: 'Invalid inventory snapshot.', detail: 'stub: bad request', errors: { items: ['stub'] } }));
        return;
      }
      const fingerprint = JSON.stringify(items.map((i) => [i.systemName, i.name, i.version, i.enabled]));
      const existing = seen.get(fingerprint);
      const id = existing || `stub-${seen.size + 1}`;
      if (!existing) seen.set(fingerprint, id);
      res.writeHead(existing ? 200 : 201, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ id, capturedAt: body.capturedAt, itemCount: items.length }));
    });
  });
  return new Promise((resolve, reject) => {
    server.on('error', reject);
    server.listen(STUB_PORT, '0.0.0.0', () => resolve({ server, requests, close: () => new Promise((r) => server.close(r)) }));
  });
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
    await expandDetails(page, 'deployseal-advanced'); // GitShaFilePath lives in the collapsed Advanced card
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
      await expandDetails(page, 'deployseal-advanced');
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

    // 7. Inventory (contract §6) against a stub API on the host ------------------------------------
    const stub = await startStub();
    try {
      log(`stub DeploySeal API listening on ${STUB_BASE} (host port ${STUB_PORT})`);
      await page.goto(BASE + '/Admin/WidgetsDeploySeal/Configure');
      await expandDetails(page, 'deployseal-platform-inventory'); // ApiKey/ApiBase/SendInventory live in the collapsed Platform inventory card
      await page.waitForSelector('#ApiKey');
      assert(await page.locator('#deployseal-inventory-notready').count() === 1, 'without an API key the inventory card must say it is not ready');
      assert(await page.locator('button[name="send-inventory"]').isDisabled(), 'without an API key "Send inventory now" must be disabled');
      await page.fill('#ApiKey', API_KEY);
      await page.fill('#ApiBase', STUB_BASE);
      await page.check('#SendInventory');
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="save"]')]);
      await page.waitForSelector('#deployseal-inventory-endpoint');
      // The key is a secret: never rendered back, only "stored" plus the mask.
      assert((await page.inputValue('#ApiKey')) === '', 'the API key box must not echo the stored key');
      assert(await page.locator('#deployseal-apikey-stored').count() === 1, 'the page must say a key is stored');
      const endpoint = (await page.locator('#deployseal-inventory-endpoint').innerText()).trim();
      assert(endpoint === `${STUB_BASE}/api/v1/sites/${SITE_KEY}/inventory`, `inventory endpoint shown: ${endpoint}`);
      const shownCount = Number((await page.locator('#deployseal-inventory-count').innerText()).trim());
      assert(shownCount >= 1, `inventory count shown: ${shownCount}`);
      assert(!(await page.locator('button[name="send-inventory"]').isDisabled()), '"Send inventory now" must be enabled once key + site key are set');
      // Take the screenshot again so it shows the inventory settings and card.
      await page.addStyleTag({ content: '.main-sidebar{display:none!important} .content-wrapper,.main-header,.main-footer{margin-left:0!important}' });
      await page.screenshot({ path: SCREENSHOT, fullPage: true });

      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="send-inventory"]')]);
      const alerts1 = (await page.locator('.alert').allTextContents()).join(' | ');
      assert(stub.requests.length === 1, `stub expected exactly one request after the first send, got ${stub.requests.length} (page said: ${alerts1})`);
      const r = stub.requests[0];
      assert(r.method === 'POST', 'method: ' + r.method);
      assert(r.url === `/api/v1/sites/${SITE_KEY}/inventory`, 'path: ' + r.url);
      assert(r.headers.authorization === `Bearer ${API_KEY}`, 'authorization header: ' + r.headers.authorization);
      assert(/^application\/json/.test(r.headers['content-type'] || ''), 'content-type: ' + r.headers['content-type']);
      assert(r.body && typeof r.body === 'object', 'body is JSON');
      assert(Object.keys(r.body).join(',') === 'platform,platformVersion,buildMarker,capturedAt,items', 'body keys: ' + Object.keys(r.body).join(','));
      assert(r.body.platform === 'nopcommerce', 'platform: ' + r.body.platform);
      assert(r.body.platformVersion === NOP_VERSION, `platformVersion: ${r.body.platformVersion} (want ${NOP_VERSION})`);
      assert(r.body.buildMarker === NOP_VERSION, `buildMarker: ${r.body.buildMarker} (want ${NOP_VERSION}; no SHA file is configured now)`);
      const capturedAgeMs = Date.now() - Date.parse(r.body.capturedAt);
      assert(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(r.body.capturedAt) && capturedAgeMs > -60_000 && capturedAgeMs < 10 * 60_000, 'capturedAt: ' + r.body.capturedAt);
      assert(Array.isArray(r.body.items) && r.body.items.length >= 1, 'items: ' + JSON.stringify(r.body.items).slice(0, 200));
      assert(r.body.items.length === shownCount, `items sent (${r.body.items.length}) must match the count shown (${shownCount})`);
      for (const item of r.body.items) {
        assert(Object.keys(item).join(',') === 'systemName,name,version,enabled', 'item keys: ' + Object.keys(item).join(','));
        assert(typeof item.systemName === 'string' && item.systemName.length > 0 && item.systemName.length <= 128, 'systemName: ' + item.systemName);
        assert(typeof item.name === 'string' && item.name.length > 0 && item.name.length <= 200, 'name: ' + item.name);
        assert(typeof item.version === 'string' && item.version.length > 0 && item.version.length <= 32, 'version: ' + item.version);
        assert(typeof item.enabled === 'boolean', 'enabled: ' + item.enabled);
      }
      const names = r.body.items.map((i) => i.systemName);
      assert(names.slice().sort().join('\n') === names.join('\n'), 'items must be sorted by systemName (ordinal)');
      assert(new Set(names).size === names.length, 'systemNames must be unique');
      const self = r.body.items.find((i) => i.systemName === 'Widgets.DeploySeal');
      assert(self && self.enabled === true && self.version === '1.2.0', 'the plugin must report itself as installed: ' + JSON.stringify(self));
      assert(/HTTP 201/.test(alerts1) && new RegExp(`${r.body.items.length} plugins`).test(alerts1), 'the page must show the 201 and the count: ' + alerts1);
      log(`inventory sent: ${r.body.items.length} plugins (${names.filter((n) => r.body.items.find((i) => i.systemName === n).enabled).length} installed), 201 shown`);

      // The same set again: the API answers 200 with the snapshot it already holds, and the page says so.
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="send-inventory"]')]);
      const alerts2 = (await page.locator('.alert').allTextContents()).join(' | ');
      assert(stub.requests.length === 2, 'second send must post once more');
      assert(/HTTP 200/.test(alerts2) && /already on record/.test(alerts2), 'the page must show the 200 as already on record: ' + alerts2);

      // The scheduled task is registered (Administration → System → Schedule tasks).
      await page.goto(BASE + '/Admin/ScheduleTask/List');
      await page.waitForSelector('text=Send platform inventory to DeploySeal', { timeout: 60_000 });

      summary.steps.push(`inventory: ${r.body.items.length} plugins posted to ${STUB_BASE}${r.url} with the bearer key (201, then 200 on resend); schedule task registered`);
      summary.inventoryRequest = { method: r.method, url: r.url, authorization: 'Bearer ' + API_KEY.slice(0, 12) + '…', platform: r.body.platform, platformVersion: r.body.platformVersion, buildMarker: r.body.buildMarker, capturedAt: r.body.capturedAt, itemCount: r.body.items.length, first: r.body.items[0] };
    } finally {
      await stub.close();
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
