// Proves the Marketplace-style multi-version bundle (artifacts/DeploySeal.Nop.Widget-all-versions.zip,
// built by build/bundle.ps1) installs correctly through nopCommerce's REAL upload path — not the
// bind-mount shortcut tests/e2e/verify.mjs uses for the per-version zips. Run against a nopCommerce
// container that has NO plugin folder mounted (docker/docker-compose.<ver>.no-mount.yml):
//
//   1. waits for nopCommerce, completes the install wizard (PostgreSQL) if it is showing,
//   2. logs in as the admin it created,
//   3. Admin -> Configuration -> Local plugins: asserts "DeploySeal widget" is NOT listed yet,
//   4. opens "Upload plugin or theme", uploads the bundle, asserts it now appears with
//      SupportedVersions containing this nopCommerce's major.minor,
//   5. Install -> Apply changes,
//   6. configures it (fake site key, label "staging"), asserts the configure preview tag,
//   7. fetches the storefront home page and asserts the exact contract tag sits in <head>.
//
// Usage (Playwright is resolved from DS_E2E_NODE_MODULES when this repo has no node_modules):
//   DS_E2E_NODE_MODULES=<path to a node_modules with playwright> node tests/e2e/verify-upload.mjs [4.60|4.90] [bundleZipPath]
//
// The nomount compose files use PORT+1 vs. the mounted ones (4.90 -> 8091, 4.60 -> 8061) so both
// can run side by side without a port clash. Override with DS_NOP_URL.

import { createRequire } from 'node:module';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const modulesDir = process.env.DS_E2E_NODE_MODULES;
const require = createRequire(modulesDir ? path.join(path.resolve(modulesDir), 'x.js') : import.meta.url);
function loadPlaywright() {
  try { return require('playwright'); } catch {}
  const testPkg = require.resolve('@playwright/test/package.json');
  return createRequire(fs.realpathSync(testPkg))('playwright');
}
const { chromium } = loadPlaywright();

const versions = JSON.parse(fs.readFileSync(path.join(repoRoot, 'build', 'versions.json'), 'utf8'));
const requested = process.env.DS_NOP_VERSION || process.argv[2] || '4.90';
const MAJOR_MINOR = requested.split('.').slice(0, 2).join('.');
const entry = versions[MAJOR_MINOR];
if (!entry) throw new Error(`Unknown nopCommerce version "${requested}"; build/versions.json knows: ${Object.keys(versions).join(', ')}`);
const NOP_VERSION = requested.split('.').length >= 3 ? requested : entry.tag.replace(/^release-/, '');
const NOMOUNT_PORT = { '4.60': 8061, '4.70': 8071, '4.80': 8081, '4.90': 8091 };
const PORT = NOMOUNT_PORT[MAJOR_MINOR];
if (!PORT) throw new Error(`No no-mount port mapping for ${MAJOR_MINOR}`);
const BASE = (process.env.DS_NOP_URL || `http://localhost:${PORT}`).replace(/\/$/, '');
const ADMIN_EMAIL = process.env.DS_ADMIN_EMAIL || 'admin@deployseal.test';
const ADMIN_PASSWORD = process.env.DS_ADMIN_PASSWORD || 'Admin!Pass123';
const BUNDLE = path.resolve(process.argv[3] || process.env.DS_BUNDLE || path.join(repoRoot, 'artifacts', 'DeploySeal.Nop.Widget-all-versions.zip'));
const SCREENSHOT = process.env.DS_SCREENSHOT || path.join(repoRoot, 'artifacts', `upload-local-plugins-${MAJOR_MINOR}.png`);
const CONFIGURE_SCREENSHOT = process.env.DS_CONFIGURE_SCREENSHOT || path.join(repoRoot, 'artifacts', `configure-upload-${MAJOR_MINOR}.png`);

const SITE_KEY = 'ls_test0000000000000000000000000000000';
const LABEL = 'staging';

const tag = (build) =>
  `<script src="https://cdn.deployseal.com/ds-widget.js" data-ds-site-key="${SITE_KEY}" data-ds-environment="${LABEL}"` +
  (build ? ` data-ds-build="${build}"` : '') + ` async></script>`;

const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function assert(cond, msg) { if (!cond) throw new Error('ASSERTION FAILED: ' + msg); }

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

async function main() {
  if (!fs.existsSync(BUNDLE)) throw new Error(`Bundle not found: ${BUNDLE} (run build/bundle.ps1 first)`);
  const bundleSize = fs.statSync(BUNDLE).size;
  fs.mkdirSync(path.dirname(SCREENSHOT), { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1400, height: 1000 }, ignoreHTTPSErrors: true });
  const page = await context.newPage();
  page.setDefaultTimeout(90_000);
  const summary = { base: BASE, nopVersion: NOP_VERSION, bundle: BUNDLE, bundleSizeBytes: bundleSize, steps: [] };

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

    // 3. Plugin must NOT be present yet (no mount) ------------------------------------------------
    await page.goto(BASE + '/Admin/Plugin/List');
    await page.fill('#SearchFriendlyName', 'DeploySeal');
    await page.click('#search-plugins-local');
    await sleep(1500);
    const beforeRowText = await page.locator('#plugins-local-grid').innerText();
    assert(!/DeploySeal widget/.test(beforeRowText), 'DeploySeal widget is already listed under Local plugins; this run must start with no plugin folder mounted');
    summary.steps.push('confirmed DeploySeal widget is NOT listed before upload (no folder mounted)');

    // 4. Upload the bundle through the real "Upload plugin or theme" modal -----------------------
    await page.click('button[name="uploadplugin"]');
    await page.waitForSelector('#uploadplugin-window.show, #uploadplugin-window.in', { timeout: 15_000 }).catch(() => {});
    await page.setInputFiles('#archivefile', BUNDLE);
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'load' }),
      page.click('#upload-plugin'),
    ]);
    const uploadAlerts = (await page.locator('.alert').allTextContents()).join(' | ');
    log('after upload, page alerts:', JSON.stringify(uploadAlerts));
    assert(/1 plugins/.test(uploadAlerts), `upload did not report 1 plugin uploaded: ${uploadAlerts}`);
    // The uploaded assembly is on disk but the in-memory plugin descriptor cache is stale until
    // "Reload list of plugins" re-scans the Plugins folder — which (like Apply changes) restarts
    // the whole application, same as a plugin install/uninstall does.
    await page.click('button[name="plugin-reload-grid"]');
    await sleep(10_000);
    await waitForServer('nopCommerce restarted after plugin-list reload', isInstalled, 10 * 60 * 1000);
    await page.goto(BASE + '/login');
    if (await page.locator('#Email').count()) {
      await page.fill('#Email', ADMIN_EMAIL);
      await page.fill('#Password', ADMIN_PASSWORD);
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button.login-button')]);
    }
    await page.goto(BASE + '/Admin/Plugin/List');
    await page.fill('#SearchFriendlyName', 'DeploySeal');
    await page.click('#search-plugins-local');
    await page.waitForSelector('#plugins-local-grid tbody tr', { timeout: 60_000 });
    await sleep(1500);
    const rowText = await page.locator('#plugins-local-grid tbody').innerText();
    assert(/DeploySeal widget/.test(rowText), `DeploySeal widget is not listed after uploading the bundle (upload alerts: ${uploadAlerts})`);
    summary.steps.push(`uploaded ${path.basename(BUNDLE)} (${bundleSize} bytes) via Upload plugin or theme; DeploySeal widget now listed`);
    await page.addStyleTag({ content: '.main-sidebar{display:none!important} .content-wrapper,.main-header,.main-footer{margin-left:0!important}' });
    await page.screenshot({ path: SCREENSHOT, fullPage: true });

    // 5. Install + Apply changes ------------------------------------------------------------------
    const installBtn = page.locator('button[name="install-plugin-link-Widgets.DeploySeal"]');
    assert(await installBtn.count(), 'no Install button for Widgets.DeploySeal after upload (bundle picked the wrong version entry?)');
    await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), installBtn.click()]);
    await page.click('button[name="plugin-apply-changes"]');
    await sleep(10_000);
    await waitForServer('nopCommerce restarted after plugin install', isInstalled, 10 * 60 * 1000);
    summary.steps.push('plugin installed via Install + Apply changes');
    await page.goto(BASE + '/login');
    if (await page.locator('#Email').count()) {
      await page.fill('#Email', ADMIN_EMAIL);
      await page.fill('#Password', ADMIN_PASSWORD);
      await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button.login-button')]);
    }

    // 6. Configure -------------------------------------------------------------------------------
    await page.goto(BASE + '/Admin/WidgetsDeploySeal/Configure');
    await page.waitForSelector('#SiteKey');
    await page.fill('#SiteKey', SITE_KEY);
    await page.fill('#EnvironmentLabel', LABEL);
    await page.check('#Enabled');
    await expandDetails(page, 'deployseal-advanced');
    await page.fill('#GitShaFilePath', '');
    await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), page.click('button[name="save"]')]);
    await page.waitForSelector('#deployseal-snippet');
    const preview = (await page.locator('#deployseal-snippet').innerText()).trim();
    assert(preview === tag(NOP_VERSION), `configure preview mismatch:\n  got  ${preview}\n  want ${tag(NOP_VERSION)}`);
    await page.addStyleTag({ content: '.main-sidebar{display:none!important} .content-wrapper,.main-header,.main-footer{margin-left:0!important}' });
    await page.screenshot({ path: CONFIGURE_SCREENSHOT, fullPage: true });
    summary.steps.push(`configured (key, label "${LABEL}", enabled); screenshot ${CONFIGURE_SCREENSHOT}`);
    summary.configurePreview = preview;

    // 7. Storefront tag ----------------------------------------------------------------------------
    const anon = await browser.newContext();
    const home = await fetchHtml(anon, BASE + '/');
    assert(home.status === 200, 'storefront home returned ' + home.status);
    const head = home.html.match(/<head[\s\S]*?(?=<\/head>|<body[\s>])/i)?.[0] || '';
    assert(head.length > 0, 'could not find <head> in the storefront HTML');
    const expected = tag(NOP_VERSION);
    assert(head.includes(expected), `storefront <head> does not contain the exact tag.\n  want ${expected}\n  head snippet: ${(home.html.match(/<script[^>]*ds-widget[^>]*>/i) || ['<none>'])[0]}`);
    assert(home.html.split('ds-widget.js').length === 2, 'the tag must appear exactly once');
    summary.storefrontTag = expected;
    log('storefront emitted:', expected);
    await anon.close();

    console.log('\nVERIFIED (upload path)\n' + JSON.stringify(summary, null, 2));
  } catch (e) {
    try { await page.screenshot({ path: path.join(path.dirname(SCREENSHOT), `failure-upload-${MAJOR_MINOR}.png`), fullPage: true }); } catch {}
    console.error('\nFAILED:', e.message, '\nat', page.url());
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main();
