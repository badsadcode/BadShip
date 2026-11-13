// Looks through a ship's windows: node windowview.mjs <dir> <name>
// Needs <name>.ship_and_interior.glb and <name>.interior.json (from `generate --interior`). Writes
// <name>_windows.png: two views from outside into lit rooms and one from inside a room out.
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
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
const wins = info.windows;
const pick = wins[Math.floor(wins.length / 2)];
const c = (w) => [w.wall_x, (w.y0 + w.y1) / 2, (w.z0 + w.z1) / 2];
const shots = [];
for (const [label, w, eyeOff, tgtOff, fov] of [
  ['outside, straight in', pick, [pick.side * 5, 0.15, -0.6], [-pick.side * 5, -0.1, 0.4], 50],
  ['outside, along the hull', pick, [pick.side * 9, 2.5, -14], [-pick.side * 1, 0, 4], 40],
  ['inside, looking out', pick, [-pick.side * 3.2, 0.1, -1.2], [pick.side * 3, 0, 0.3], 60],
]) {
  const p = c(w);
  const eye = [p[0] + eyeOff[0], p[1] + eyeOff[1], p[2] + eyeOff[2]];
  const target = [p[0] + tgtOff[0], p[1] + tgtOff[1], p[2] + tgtOff[2]];
  const img = await page.evaluate(([u, e, t, f]) => window.renderShip(u, { w: 720, h: 460, eye: e, target: t, fov: f }), [`/data/${name}.ship_and_interior.glb`, eye, target, fov]);
  shots.push({ label, img });
}
const html = `<body style="margin:0;background:#0b0f14;font:14px sans-serif;color:#cfd6de;display:flex;gap:8px;padding:8px">${shots.map(s => `<div><img src="${s.img}" width="720" height="460"><div>${s.label}</div></div>`).join('')}</body>`;
const sheet = await browser.newPage({ viewport: { width: 2200, height: 500 } });
await sheet.setContent(html);
await sheet.screenshot({ path: join(dir, `${name}_windows.png`) });
console.log('wrote', join(dir, `${name}_windows.png`));
await browser.close(); server.close();
