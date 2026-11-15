// Looks at the less grid-like layouts: node layoutview.mjs <dir> <name> <deck> <z0> <z1> <out.png> [eyeZ]
// [eyeZ] (where the corridor shot stands). Needs <name>.interior.deck_N.glb and <name>.interior.json. Draws the stretch of the deck from z0 to z1 from above
// (roof cut away, at an angle), a look down its corridor at eye height from z1 towards z0, and a look into an alcove.
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name, deckArg, z0Arg, z1Arg, out, eyeArg] = process.argv.slice(2);
const dir = resolve(dirArg);
const info = JSON.parse(readFileSync(join(dir, `${name}.interior.json`), 'utf8'));
const deck = Number(deckArg), z0 = Number(z0Arg), z1 = Number(z1Arg);
const fy = info.decks[deck].floor_y;
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : url.startsWith('/data/') ? join(dir, url.slice(6)) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': extname(file) === '.html' ? 'text/html' : extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ deviceScaleFactor: 2 });
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
const glb = `/data/${name}.interior.deck_${deck}.glb`;
const zm = (z0 + z1) / 2, span = Math.abs(z1 - z0);
const views = [
  ['from above, roof off', { w: 1440, h: 560, eye: [-span * 0.1, fy + span * 0.95, zm], target: [0, fy, zm], fov: 36, clip: [0, -1, 0, fy + 2.2] }],
  ['down the corridor', { w: 720, h: 460, eye: [0, fy + 1.65, eyeArg ? Number(eyeArg) : z1], target: [0.6, fy + 1.3, z0], fov: 70 }],
];
// An alcove on this deck in the stretch, seen from the corridor across from it.
const al = info.rooms.find(r => r.kind === 'alcove' && r.deck === deck && r.min[2] > Math.min(z0, z1) && r.max[2] < Math.max(z0, z1));
if (al) {
  // Stand in the corridor segment beside it (the one whose side it opens on), a few metres along, and look in.
  const ax = (al.min[0] + al.max[0]) / 2, az = (al.min[2] + al.max[2]) / 2, s = Math.sign(ax);
  const seg = info.rooms.find(r => r.kind === 'corridor' && r.deck === deck && r.min[2] <= az && r.max[2] >= az);
  const cx = seg ? (seg.min[0] + seg.max[0]) / 2 : 0;
  views.push([`into an alcove`, { w: 720, h: 460, eye: [cx - s * 0.6, fy + 1.65, az - 3.5], target: [ax + s * 1.5, fy + 1.1, az + 0.5], fov: 75 }]);
}
const imgs = [];
for (const [label, o] of views) imgs.push({ label, img: await page.evaluate(([u, o]) => window.renderShip(u, o), [glb, o]) });
await page.setViewportSize({ width: 1460, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:14px system-ui;color:#c9d1d9;padding:8px">
  <div>${imgs[0].label}</div><img src="${imgs[0].img}" width=1440><div style="display:flex;gap:0">${imgs.slice(1).map(x => `<div><div>${x.label}</div><img src="${x.img}" width=720></div>`).join('')}</div></body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
