// A faction's ships side by side, each framed on its own (not to scale), one row per folder:
// node familyview.mjs <out.png> <label=dir>... -- <name>...
// e.g. node familyview.mjs fam.png before=out/a after=out/b -- warship patrol freighter research
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const out = args[0];
const sep = args.indexOf('--');
const rows = args.slice(1, sep).map(a => { const [label, dir] = a.split('='); return { label, dir: resolve(dir) }; });
const names = args.slice(sep + 1);
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const m = url.match(/^\/row(\d+)\/(.*)$/);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : m ? join(rows[+m[1]].dir, m[2]) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': extname(file) === '.html' ? 'text/html' : extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ deviceScaleFactor: 2 });
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
const grid = [];
for (let i = 0; i < rows.length; i++) {
  const imgs = [];
  for (const n of names) imgs.push(await page.evaluate(u => window.renderShip(u, { w: 360, h: 240, view: 'quarter' }), `/row${i}/${n}.glb`));
  grid.push({ label: rows[i].label, imgs });
}
const w = 120 + names.length * 368;
await page.setViewportSize({ width: w, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:15px system-ui;color:#c9d1d9;padding:8px">${grid.map(r => `<div style="display:flex;gap:8px;align-items:center;margin-bottom:8px"><div style="width:104px">${r.label}</div>${r.imgs.map(i => `<img src="${i}" width=360>`).join('')}</div>`).join('')}</body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
