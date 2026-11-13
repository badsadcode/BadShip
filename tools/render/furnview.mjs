// Looks at the furniture: node furnview.mjs <dir> <name> <out.png> [kinds...]
// For each room kind asked for (default: a few), the room from above with the ceiling cut away and from inside at eye
// height. Needs <name>.interior.glb and <name>.interior.json (from `generate --interior`).
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name, out, ...want] = process.argv.slice(2);
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
const kinds = want.length ? want : ['bridge', 'crew_quarters', 'mess', 'engineering'];
const glb = `/data/${name}.interior.glb`;
const cells = [];
for (const kind of kinds) {
  const rooms = info.rooms.filter(r => r.kind === kind).sort((a, b) => (b.max[0] - b.min[0]) * (b.max[2] - b.min[2]) - (a.max[0] - a.min[0]) * (a.max[2] - a.min[2]));
  const room = rooms[0];
  if (!room) continue;
  const fy = info.decks[room.deck].floor_y;
  const c = [(room.min[0] + room.max[0]) / 2, fy, (room.min[2] + room.max[2]) / 2];
  const span = Math.max(room.max[0] - room.min[0], room.max[2] - room.min[2]) + 3;
  const top = await page.evaluate(([u, e, t, cl]) => window.renderShip(u, { w: 560, h: 400, eye: e, target: t, fov: 40, clip: cl }),
    [glb, [c[0], fy + span * 1.35, c[2] - 0.01], c, [0, -1, 0, fy + 2.3]]);
  // Inside: standing in the middle of the room (always kept clear), looking at one end and a side wall.
  const eye = [c[0], fy + 1.65, c[2]];
  const tgt = kind === 'bridge' ? [room.min[0] + 0.25 * (room.max[0] - room.min[0]), fy + 0.8, room.max[2]] : [room.max[0], fy + 0.8, c[2] + 0.3 * (room.max[2] - c[2])];
  const inside = await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 560, h: 400, eye: e, target: t, fov: 75 }), [glb, eye, tgt]);
  cells.push({ label: kind.replace('_', ' '), imgs: [top, inside] });
}
await page.setViewportSize({ width: 1180, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:15px system-ui;color:#c9d1d9;padding:10px">${cells.map(c => `<div style="margin:6px 0">${c.label}<div style="display:flex;gap:10px">${c.imgs.map(i => `<img src="${i}" width=560>`).join('')}</div></div>`).join('')}</body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
