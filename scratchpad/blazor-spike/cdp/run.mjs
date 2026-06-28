import puppeteer from 'puppeteer-core';

const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const URL = process.env.SPIKE_URL || 'http://127.0.0.1:5280/';
const OUT = process.env.SPIKE_OUT || '/Users/rodrigooliveira/git/roo-oliv/monodreams/.worktrees/feat/kni/scratchpad/blazor-spike/shot-final.png';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const browser = await puppeteer.launch({
  executablePath: CHROME,
  headless: 'new',
  args: [
    '--no-sandbox',
    '--window-size=800,600',
    // Software WebGL — headless macOS has no real GPU context.
    '--use-gl=angle',
    '--use-angle=swiftshader',
    '--enable-unsafe-swiftshader',
    '--ignore-gpu-blocklist',
  ],
});

const page = await browser.newPage();
await page.setViewport({ width: 800, height: 600 });

const logs = [];
page.on('console', (m) => logs.push(`[console.${m.type()}] ${m.text()}`));
page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));
page.on('requestfailed', (r) => logs.push(`[requestfailed] ${r.url()} ${r.failure()?.errorText}`));
page.on('response', (r) => { if (r.status() >= 400) logs.push(`[http ${r.status()}] ${r.url()}`); });

// The KNI game loop drives requestAnimationFrame forever, so the page never goes
// network/animation idle; wait only for the document, then poll the canvas below.
await page.goto(URL, { waitUntil: 'domcontentloaded', timeout: 120000 });

// Wait for the WASM runtime to boot and the canvas page to render, then for the WebGL
// canvas to actually contain non-uniform pixels (i.e. the sprite was drawn).
let canvasReport = null;
for (let i = 0; i < 60; i++) {
  await sleep(1000);
  canvasReport = await page.evaluate(() => {
    const loading = document.getElementById('loading');
    const canvas = document.getElementById('theCanvas');
    if (!canvas) return { stage: 'no-canvas', loadingVisible: !!loading };
    const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
    const w = canvas.width, h = canvas.height;
    let nonUniform = false, distinctColors = 0, sample = null, glerr = null;
    if (gl && w > 0 && h > 0) {
      try {
        const px = new Uint8Array(w * h * 4);
        gl.readPixels(0, 0, w, h, gl.RGBA, gl.UNSIGNED_BYTE, px);
        glerr = gl.getError();
        const seen = new Set();
        const first = px.slice(0, 4).join(',');
        for (let p = 0; p < px.length; p += 4) {
          const key = `${px[p]},${px[p+1]},${px[p+2]}`;
          seen.add(key);
          if (px.slice(p, p + 4).join(',') !== first) nonUniform = true;
          if (seen.size > 64) break;
        }
        distinctColors = seen.size;
        sample = first;
      } catch (e) { glerr = 'readPixels threw: ' + e.message; }
    }
    return {
      stage: 'canvas',
      w, h,
      loadingPresent: !!loading,
      glContext: gl ? (gl instanceof WebGL2RenderingContext ? 'webgl2' : 'webgl') : 'none',
      glError: glerr,
      nonUniform,
      distinctColors,
      sampleTopLeftRGBA: sample,
    };
  });
  // Done once we have a GL context with a non-uniform (rendered) frame.
  if (canvasReport.stage === 'canvas' && canvasReport.nonUniform) break;
  if (canvasReport.stage === 'canvas' && canvasReport.glContext === 'none' && i > 8) break;
}

// Authoritative observation: the page screenshot composites the live presented canvas
// (WebGL preserveDrawingBuffer is false, so an in-page readPixels of the default
// framebuffer reads cleared 0,0,0,0 — a measurement artifact, not a render failure).
const shotBuf = Buffer.from(await page.screenshot({ encoding: 'binary' }));
const { writeFileSync } = await import('node:fs');
writeFileSync(OUT, shotBuf);

// Decode the PNG enough to count distinct colors and confirm a sprite (not just bg) rendered.
// Minimal zlib-inflate PNG reader to avoid extra deps.
function analyzePng(buf) {
  const zlib = require('node:zlib');
  let pos = 8; // skip signature
  let width = 0, height = 0, bitDepth = 0, colorType = 0;
  const idat = [];
  while (pos < buf.length) {
    const len = buf.readUInt32BE(pos); const type = buf.toString('ascii', pos + 4, pos + 8);
    const data = buf.subarray(pos + 8, pos + 8 + len);
    if (type === 'IHDR') { width = data.readUInt32BE(0); height = data.readUInt32BE(4); bitDepth = data[8]; colorType = data[9]; }
    else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    pos += 12 + len;
  }
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const channels = colorType === 6 ? 4 : colorType === 2 ? 3 : 1;
  const stride = width * channels;
  const colors = new Set();
  let prev = Buffer.alloc(stride);
  let rowStart = 0;
  const out = Buffer.alloc(height * stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[rowStart]; rowStart++;
    const row = out.subarray(y * stride, (y + 1) * stride);
    for (let x = 0; x < stride; x++) {
      const rawByte = raw[rowStart + x];
      const a = x >= channels ? row[x - channels] : 0;
      const b = prev[x];
      const c = x >= channels ? prev[x - channels] : 0;
      let val;
      switch (filter) {
        case 0: val = rawByte; break;
        case 1: val = rawByte + a; break;
        case 2: val = rawByte + b; break;
        case 3: val = rawByte + ((a + b) >> 1); break;
        case 4: {
          const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
          val = rawByte + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c); break;
        }
        default: val = rawByte;
      }
      row[x] = val & 0xff;
    }
    rowStart += stride;
    prev = row;
    for (let x = 0; x < stride; x += channels) colors.add(`${row[x]},${row[x+1]},${row[x+2]}`);
  }
  return { width, height, distinctColors: colors.size, sampleColors: [...colors].slice(0, 8) };
}
const { createRequire } = await import('node:module');
const require = createRequire(import.meta.url);
const screenshotReport = analyzePng(shotBuf);

console.log('=== SCREENSHOT REPORT (authoritative) ===');
console.log(JSON.stringify(screenshotReport, null, 2));
const PASS = screenshotReport.distinctColors >= 3; // bg + 2 checker colors at minimum
console.log('=== RENDER PASS: ' + PASS + ' (>=3 distinct colors means sprite + bg rendered) ===');
console.log('=== IN-PAGE CANVAS REPORT (informational; readback artifact) ===');
console.log(JSON.stringify(canvasReport, null, 2));
console.log('=== PAGE LOGS (' + logs.length + ') ===');
console.log(logs.join('\n'));

await browser.close();
