// Deck plans from above, roof cut away: node planview.mjs <dir> <name> <out.png> [deck...]
// Needs <name>.interior.glb and <name>.interior.json. Each deck seen straight down, clipped at 2 m over its floor,
// along a window as long as the deck (bow to the right).
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
const decks = want.length ? want.map(Number) : info.decks.filter(d => d.part === 'hull').map(d => d.index);
const imgs = [];
for (const i of decks) {
  const d = info.decks[i];
  imgs.push({ label: `deck ${i} (${d.part})`, img: await page.evaluate(([u, c]) => window.renderShip(u, { w: 1400, h: 300, view: 'top', clip: c }),
    [`/data/${name}.interior.deck_${i}.glb`, [0, -1, 0, d.floor_y + 2]]) });
}
await page.setViewportSize({ width: 1420, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:14px system-ui;color:#c9d1d9;padding:8px">${imgs.map(x => `<div>${x.label}</div><img src="${x.img}" width=1400>`).join('')}</body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
