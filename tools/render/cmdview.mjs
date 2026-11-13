// A spine ship's command section, cut open down its middle: node cmdview.mjs <dir> <name> <out.png>
// Needs <name>.ship_and_interior.glb and <name>.metadata.json (from `generate --interior`). Two views: the front of the
// ship from outside, and the command section and what stands on it cut open, seen from the side: the spine's decks
// running into it, its own decks stacked up, and the bridge on top.
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
const meta = JSON.parse(readFileSync(join(dir, `${name}.metadata.json`), 'utf8'));
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
const cmd = meta.modules.find(m => m.type === 'command_section');
if (!cmd) { console.log('no command section'); process.exit(1); }
// Everything standing on it, up to the top of the bridge.
const top = Math.max(...meta.modules.filter(m => m.bounds.min[2] < cmd.bounds.max[2] && m.bounds.max[2] > cmd.bounds.min[2] && Math.abs((m.bounds.min[0] + m.bounds.max[0]) / 2) < 6 && m.type !== 'details' && m.type !== 'livery').map(m => m.bounds.max[1]));
const z0 = cmd.bounds.min[2] - 10, z1 = cmd.bounds.max[2] - (cmd.bounds.max[2] - cmd.bounds.min[2]) * 0.4;
const y0 = cmd.bounds.min[1], y1 = Math.min(top, cmd.bounds.max[1] + 40);
const zc = (z0 + z1) / 2, yc = (y0 + y1) / 2, span = Math.max(z1 - z0, (y1 - y0) * 1.5);
const glb = `/data/${name}.ship_and_interior.glb`;
const a = await page.evaluate(([u]) => window.renderShip(u, { w: 720, h: 520, view: 'quarter' }), [glb]);
const b = await page.evaluate(([u, e, t, c]) => window.renderShip(u, { w: 720, h: 520, eye: e, target: t, fov: 40, clip: c }), [glb, [span * 1.0, yc, zc], [0, yc, zc], [-1, 0, 0, 0]]);
await page.setViewportSize({ width: 1480, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:15px system-ui;color:#c9d1d9;padding:10px"><div style="display:flex;gap:10px"><div><img src="${a}" width=720><div>the ship</div></div><div><img src="${b}" width=720><div>its command section cut open: the spine's decks run into it, its own decks stack up to the bridge</div></div></div></body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
