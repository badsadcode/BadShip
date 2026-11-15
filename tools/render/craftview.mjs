// Close-ups of parked craft: node craftview.mjs <dir> <out.png> <name>...
// For each ship, its first parked craft (type "shuttle") seen from outside: through a side hangar's mouth, or from above
// a flight deck. Needs <name>.glb and <name>.metadata.json.
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
const page = await browser.newPage({ deviceScaleFactor: 2 });
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
const cells = [];
for (const name of names) {
  const meta = JSON.parse(readFileSync(join(dir, `${name}.metadata.json`), 'utf8'));
  const craft = meta.modules.filter(m => m.type === 'shuttle');
  if (!craft.length) continue;
  const parent = meta.modules.find(m => m.id === craft[0].parent);
  const deck = parent && parent.type === 'flight_deck';
  const b = deck ? { min: [0, 1, 2].map(i => Math.min(...craft.map(c => c.bounds.min[i]))), max: [0, 1, 2].map(i => Math.max(...craft.map(c => c.bounds.max[i]))) } : craft[0].bounds;
  const c = [0, 1, 2].map(i => (b.min[i] + b.max[i]) / 2);
  const size = Math.max(...[0, 1, 2].map(i => b.max[i] - b.min[i]));
  const out = Math.sign(c[0]) || 1;
  const eye = deck ? [c[0] - size * 0.9, c[1] + size * 0.55, c[2] - size * 0.35] : [c[0] + out * size * 1.6, c[1] + size * 0.35, c[2] - size * 0.5];
  cells.push({ label: `${name}: ${craft.length} ${craft[0].extras?.kind ?? 'craft'}${deck ? ' on the flight deck' : ' in a side hangar'}`,
    img: await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 560, h: 380, eye: e, target: t, fov: 50 }), [`/data/${name}.glb`, eye, c]) });
}
await page.setViewportSize({ width: 1170, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:14px system-ui;color:#c9d1d9;padding:8px"><div style="display:flex;flex-wrap:wrap;gap:10px">${cells.map(x => `<div><img src="${x.img}" width=560><div>${x.label}</div></div>`).join('')}</div></body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
