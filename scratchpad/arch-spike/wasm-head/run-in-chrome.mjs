// Automated driver for the wave-0 WASM target proof (issue #119, contract item 2).
//
// A Blazor Release *publish* trims, so "the bundle built" is a weaker claim than "the bundle runs" —
// the NativeAOT leg of this same spike proves the gap is real (without component registration it
// publishes fine and then dies on the first world.Create). This script closes that gap for the web
// target: it loads the published bundle in headless Chrome, waits for Program.Main to report, and
// exits non-zero unless every check passed.
//
// Same shape as scratchpad/blazor-spike/cdp/run.mjs (the established precedent in this repo):
// puppeteer-core driving the SYSTEM Chrome, no bundled browser download.
//
//   PUPPETEER_SKIP_DOWNLOAD=1 npm i puppeteer-core@23      # anywhere; NODE_PATH can point at it
//   node run-in-chrome.mjs                                 # prints the report, exits 0 on PASS
//
// Env: SPIKE_URL (default http://127.0.0.1:5291/index.html), CHROME (default macOS system Chrome).

import puppeteer from 'puppeteer-core';

const CHROME = process.env.CHROME || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const URL = process.env.SPIKE_URL || 'http://127.0.0.1:5291/index.html';
const TIMEOUT_MS = Number(process.env.SPIKE_TIMEOUT_MS || 120000);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const browser = await puppeteer.launch({
  executablePath: CHROME,
  headless: 'new',
  args: ['--no-sandbox', '--window-size=900,700'],
});

let exitCode = 1;
try {
  const page = await browser.newPage();

  const logs = [];
  page.on('console', (m) => logs.push(`[console.${m.type()}] ${m.text()}`));
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));
  page.on('requestfailed', (r) => logs.push(`[requestfailed] ${r.url()} ${r.failure()?.errorText}`));
  page.on('response', (r) => { if (r.status() >= 400) logs.push(`[http ${r.status()}] ${r.url()}`); });

  await page.goto(URL, { waitUntil: 'domcontentloaded', timeout: TIMEOUT_MS });

  // The head sets document.title once the exercise has run; polling it is more robust than any
  // network/animation idle heuristic, because the .NET WASM runtime keeps working after load.
  const deadline = Date.now() + TIMEOUT_MS;
  let title = '';
  while (Date.now() < deadline) {
    title = await page.title();
    if (title === 'ARCH-WASM PASS' || title === 'ARCH-WASM FAIL') break;
    await sleep(500);
  }

  const report = await page.$eval('#report', (el) => el.textContent);
  console.log(report);
  console.log('--- browser log ---');
  console.log(logs.join('\n') || '(empty)');
  console.log('--- document.title ---');
  console.log(title || '(never set — the runtime did not reach Program.Main)');

  exitCode = title === 'ARCH-WASM PASS' ? 0 : 1;
} finally {
  await browser.close();
}

process.exit(exitCode);
