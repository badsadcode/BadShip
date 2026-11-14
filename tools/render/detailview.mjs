// Close-ups of a ship's surface, where detail shows: node detailview.mjs <dir> <out.png> <name>...
// For each ship, three shots near the main hull: along its side, down onto its back and up at its belly, at a distance a game camera
// might fly by at. Needs <name>.glb and <name>.metadata.json.
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, out, ...names] = process.argv.slice(2);
const dir = resolve(dirArg);
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : url.startsWith('/data/') ? join(dir, url.slice(6)) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': extname(file) === '.html' ? 'text/html' : extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ deviceScaleFactor: 1 });
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
const rows = [];
for (const name of names) {
  const meta = JSON.parse(readFileSync(join(dir, `${name}.metadata.json`), 'utf8'));
  const hull = meta.modules.filter(m => m.tier === 'primary').sort((a, b) => (b.bounds.max[2] - b.bounds.min[2]) - (a.bounds.max[2] - a.bounds.min[2]))[0];
  const [mn, mx] = [hull.bounds.min, hull.bounds.max];
  const c = [0, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
  const len = mx[2] - mn[2], wid = mx[0] - mn[0], hei = mx[1] - mn[1];
  const d = Math.max(18, Math.min(60, len * 0.22));
  const glb = `/data/${name}.glb`;
  const side = await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 640, h: 400, eye: e, target: t, fov: 45 }), [glb, [mx[0] + d, c[1] + hei * 0.15, c[2] - d * 0.6], [mx[0] * 0.5, c[1], c[2] + d * 0.4]]);
  const top = await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 640, h: 400, eye: e, target: t, fov: 45 }), [glb, [wid * 0.4, mx[1] + d * 0.8, c[2] - d * 0.9], [0, mx[1], c[2] + d * 0.3]]);
  const below = await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 640, h: 400, eye: e, target: t, fov: 45 }), [glb, [mx[0] + d * 0.5, mn[1] - d * 0.7, c[2] - d * 0.8], [0, mn[1], c[2] + d * 0.3]]);
  rows.push({ name, imgs: [side, top, below] });
}
await page.setViewportSize({ width: 1960, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:14px system-ui;color:#c9d1d9;padding:8px">${rows.map(r => `<div>${r.name}<div style="display:flex;gap:8px">${r.imgs.map(i => `<img src="${i}" width=640>`).join('')}</div></div>`).join('')}</body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
