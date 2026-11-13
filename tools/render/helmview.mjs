// Looks at a bridge in a superstructure block: node helmview.mjs <dir> <name> <out.png>
// Needs <name>.ship_and_interior.glb, <name>.interior.glb and <name>.interior.json (from `generate --interior`).
// Three views: the block cut open down its middle (the decks stacked in it and the bridge at the glass), the view from
// the back of the bridge out through the front glass, and the front of the block from outside.
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name, out] = process.argv.slice(2);
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
const page = await browser.newPage({ deviceScaleFactor: 2 });
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
// The highest bridge outside the hull.
const bridge = info.rooms.filter(r => r.kind === 'bridge' && info.decks[r.deck].part === 'superstructure').sort((a, b) => b.min[1] - a.min[1])[0];
if (!bridge) { console.log('no superstructure bridge'); process.exit(1); }
const d = info.decks[bridge.deck];
const cx = (bridge.min[0] + bridge.max[0]) / 2;
const shots = [];
// 1. Cut open down the middle, seen from the side: every deck under the bridge in this block.
const stack = info.decks.filter(x => x.part === 'superstructure' && x.z1 > bridge.min[2] - 2 && x.z0 < bridge.max[2] + 2 && x.floor_y > d.floor_y - 12 && Math.abs(x.cx - d.cx) < 1);
const yLo = Math.min(...stack.map(x => x.floor_y)) - 2, yHi = d.ceiling_y + 3;
const zc = (Math.min(...stack.map(x => x.z0)) + bridge.max[2]) / 2;
const span = Math.max(yHi - yLo, (bridge.max[2] - Math.min(...stack.map(x => x.z0))) * 0.7) + 2;
shots.push({ label: `cut open: ${stack.length} decks in the bridge block, the bridge on top, at the glass`, img: await page.evaluate(([u, e, t, c]) => window.renderShip(u, { w: 720, h: 480, eye: e, target: t, fov: 45, clip: c }),
  [`/data/${name}.ship_and_interior.glb`, [cx + span * 1.25, (yLo + yHi) / 2, zc], [cx, (yLo + yHi) / 2, zc], [-1, 0, 0, cx]]) });
// 2. From the back of the bridge, looking out of the front glass.
shots.push({ label: 'from the back of the bridge, out through the front glass', img: await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 720, h: 480, eye: e, target: t, fov: 75 }),
  [`/data/${name}.ship_and_interior.glb`, [cx, d.floor_y + 1.65, bridge.min[2] + 0.6], [cx, d.floor_y + 1.4, bridge.max[2] + 10]]) });
// 3. The front of the block from outside, a little to one side.
shots.push({ label: 'from outside', img: await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 720, h: 480, eye: e, target: t, fov: 45 }),
  [`/data/${name}.ship_and_interior.glb`, [cx + 9, d.floor_y + 4, bridge.max[2] + 22], [cx, d.floor_y + 1.5, bridge.max[2]]]) });
await page.setViewportSize({ width: 1480, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:15px system-ui;color:#c9d1d9;padding:10px"><div style="display:flex;flex-wrap:wrap;gap:10px">${shots.map(s => `<div><img src="${s.img}" width=720><div>${s.label}</div></div>`).join('')}</div></body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
