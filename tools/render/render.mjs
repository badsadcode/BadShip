// Renders a BadShip gallery folder (index.json + .glb files) into contact sheets with three.js in headless Chromium.
//
//   node render.mjs <galleryDir> [--cols 4] [--width 560] [--views quarter,side] [--each]
//
// Writes <galleryDir>/contact_sheet.png (beauty, 3/4 view), <galleryDir>/silhouettes.png (side + top silhouettes)
// and, with --each, <galleryDir>/<name>.png per ship.
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));

const args = process.argv.slice(2);
const dir = resolve(args.find(a => !a.startsWith('--')) ?? '../../out/gallery');
const opt = (k, d) => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : d; };
const cols = +opt('cols', 4);
const cellW = +opt('width', 560);
const each = args.includes('--each');
const view = opt('view', 'quarter');
const index = JSON.parse(readFileSync(join(dir, 'index.json'), 'utf8'));

const types = { '.html': 'text/html', '.js': 'text/javascript', '.glb': 'model/gltf-binary', '.json': 'application/json' };
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  let file;
  if (url.startsWith('/three/')) file = join(here, 'node_modules/three', url.slice(7));
  else if (url.startsWith('/data/')) file = join(dir, url.slice(6));
  else file = join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
  res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const port = server.address().port;

const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
page.on('pageerror', e => console.error('page error:', e.message));
await page.goto(`http://127.0.0.1:${port}/`);
await page.waitForFunction(() => window.ready === true);

const cellH = Math.round(cellW * 9 / 16);
const beauty = [], silhouettes = [];
for (const s of index) {
  const url = `/data/${s.name}.glb`;
  const quarter = await page.evaluate(([u, w, h, v]) => window.renderShip(u, { w, h, view: v }), [url, cellW * 2, cellH * 2, view]);
  beauty.push({ ...s, images: [quarter] });
  const side = await page.evaluate(([u, w, h]) => window.renderShip(u, { w, h, view: 'side', mode: 'silhouette' }), [url, cellW, Math.round(cellW * 0.32)]);
  const top = await page.evaluate(([u, w, h]) => window.renderShip(u, { w, h, view: 'top', mode: 'silhouette' }), [url, cellW, Math.round(cellW * 0.32)]);
  silhouettes.push({ ...s, images: [side, top] });
  if (each) writeFileSync(join(dir, `${s.name}.png`), Buffer.from(quarter.split(',')[1], 'base64'));
  process.stdout.write('.');
}
console.log();

async function sheet(title, cells, file) {
  await page.evaluate(([t, c, n, w]) => window.buildSheet(t, c, n, w), [title, cells, cols, cellW]);
  const el = await page.$('#sheet');
  await el.screenshot({ path: join(dir, file) });
  console.log('wrote', join(dir, file));
}
await sheet(`BadShip gallery — ${index.length} seeds (${view} view)`, beauty, 'contact_sheet.png');
await sheet('Silhouettes — side and top', silhouettes, 'silhouettes.png');

await browser.close();
server.close();
