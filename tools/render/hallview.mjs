// Looks round a ship's complex interior: node hallview.mjs <dir> <name>
// Needs <name>.interior.glb, <name>.interior.deck_N.glb and <name>.interior.json (from `generate --interior`).
// Writes <name>_halls.png: a two-deck hall from its floor and from its gallery, the gallery deck from above, and a
// ring corridor from above (when the ship has them).
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name] = process.argv.slice(2);
const dir = resolve(dirArg);
const info = JSON.parse(readFileSync(join(dir, `${name}.interior.json`), 'utf8'));
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : url.startsWith('/data/') ? join(dir, url.slice(6)) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': extname(file) === '.html' ? 'text/html' : extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage();
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);

const views = [];
const gallery = info.rooms.find(r => r.kind === 'gallery');
if (gallery) {
  const hall = info.rooms.find(r => r.deck === gallery.deck - 1 && r.max[1] > gallery.min[1] + 1);
  const [a, b, c] = [hall.min[2], hall.max[2], hall.max[0]];
  const fy = hall.min[1], gy = gallery.min[1];
  views.push([`${hall.kind.replace('_', ' ')}, two decks tall, from its floor`, `${name}.interior.glb`, [c * 0.55, fy + 1.7, a + 1.2], [-c * 0.1, fy + 2.6, b - 3], 70]);
  views.push(['from the gallery, looking down', `${name}.interior.glb`, [-(c - 0.8), gy + 1.7, a + 0.8], [c * 0.3, fy + 0.5, (a + b) / 2 + 2], 70]);
  views.push(['the gallery deck from above', `${name}.interior.deck_${gallery.deck}.glb`, [c * 1.6, gy + 22, a - 10], [0, gy, (a + b) / 2], 50]);
}
const ring = info.rooms.find(r => r.id.endsWith('_aft') && r.id.startsWith('ring_'));
if (ring) {
  const fwd = info.rooms.find(r => r.id === ring.id.replace('_aft', '_fwd'));
  const mid = (ring.min[2] + fwd.max[2]) / 2, fy = ring.min[1], c = ring.max[0];
  views.push(['a ring corridor round a central room', `${name}.interior.deck_${ring.deck}.glb`, [c * 1.4, fy + 26, mid - 22], [0, fy, mid], 50]);
}
const shots = [];
for (const [label, file, eye, target, fov] of views) {
  const img = await page.evaluate(([u, e, t, f]) => window.renderShip(u, { w: 720, h: 460, eye: e, target: t, fov: f }), [`/data/${file}`, eye, target, fov]);
  shots.push({ label, img });
}
const html = `<body style="margin:0;background:#0b0f14;font:14px sans-serif;color:#cfd6de;display:grid;grid-template-columns:720px 720px;gap:8px;padding:8px">${shots.map(s => `<div><img src="${s.img}" width="720" height="460"><div>${s.label}</div></div>`).join('')}</body>`;
const sheet = await browser.newPage({ viewport: { width: 1460, height: 980 } });
await sheet.setContent(html);
await sheet.screenshot({ path: join(dir, `${name}_halls.png`), fullPage: true });
console.log('wrote', join(dir, `${name}_halls.png`), shots.length, 'views');
await browser.close(); server.close();
