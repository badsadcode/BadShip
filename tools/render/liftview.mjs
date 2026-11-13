// Looks at how a ship's decks join up: node liftview.mjs <dir> <name>
// Needs <name>.interior.glb and <name>.interior.json (from `generate --interior`). Writes <name>_lifts.png: the whole
// interior from the side (every deck, the hull's, the superstructure's and the keel's), a ladder from below, and the
// elevator car from inside its shaft (when the ship has them).
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
const shots = [];
const glb = `/data/${name}.interior.glb`;
const parts = info.decks.map(d => d.part).join(', ');
shots.push({ label: `all ${info.decks.length} decks from the side (${parts})`, img: await page.evaluate(u => window.renderShip(u, { w: 720, h: 460, view: 'side', mode: 'clay' }), glb) });
const ladder = info.lifts.find(l => l.kind === 'ladder');
if (ladder) {
  const [x0, x1, z0, z1] = ladder.shaft;
  const lo = info.decks[ladder.decks[0]];
  const eye = [x1 + 1.6, lo.floor_y + 1.2, (z0 + z1) / 2 - 2.2];
  const target = [x0, lo.floor_y + 3.2, (z0 + z1) / 2];
  shots.push({ label: 'a ladder up through the hatch', img: await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 720, h: 460, eye: e, target: t, fov: 75 }), [glb, eye, target]) });
}
const lift = info.lifts.find(l => l.kind === 'elevator');
if (lift) {
  const [x0, x1, z0, z1] = lift.shaft;
  const top = info.decks[lift.decks.at(-1)], lo = info.decks[lift.decks[0]];
  // Cut open along the shaft (everything outboard of its middle removed) and seen from outside.
  const xc = (x0 + x1) / 2, zc = (z0 + z1) / 2, ym = (lo.floor_y + top.floor_y) / 2 + 1.5;
  const span = top.floor_y - lo.floor_y + 6;
  const eye = [xc + span * 1.1, ym, zc - span * 0.35];
  const target = [xc - 2, ym, zc];
  shots.push({ label: `the elevator, cut open: ${lift.decks.length} stops, car at the bottom`, img: await page.evaluate(([u, e, t, c]) => window.renderShip(u, { w: 720, h: 460, eye: e, target: t, fov: 55, clip: c }), [glb, eye, target, [-1, 0, 0, xc]]) });
}
const html = `<body style="margin:0;background:#0b0f14;font:14px sans-serif;color:#cfd6de;display:grid;grid-template-columns:720px 720px;gap:8px;padding:8px">${shots.map(s => `<div><img src="${s.img}" width="720" height="460"><div>${s.label}</div></div>`).join('')}</body>`;
const sheet = await browser.newPage({ viewport: { width: 1460, height: 980 } });
await sheet.setContent(html);
await sheet.screenshot({ path: join(dir, `${name}_lifts.png`), fullPage: true });
console.log('wrote', join(dir, `${name}_lifts.png`), shots.length, 'views');
await browser.close(); server.close();
