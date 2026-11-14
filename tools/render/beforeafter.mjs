// The same ships from two generator versions, side by side, close up: node beforeafter.mjs <dirBefore> <dirAfter> <out.png> <name>...
// For each ship: along its side and down onto its back, before (left) and after (right). Needs <name>.glb and
// <name>.metadata.json in both folders.
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [beforeArg, afterArg, out, ...names] = process.argv.slice(2);
const dirs = { before: resolve(beforeArg), after: resolve(afterArg) };
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const m = url.match(/^\/(before|after)\/(.*)$/);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : m ? join(dirs[m[1]], m[2]) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': extname(file) === '.html' ? 'text/html' : extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ deviceScaleFactor: 2 });
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
const rows = [];
for (const name of names) {
  const meta = JSON.parse(readFileSync(join(dirs.after, `${name}.metadata.json`), 'utf8'));
  const hull = meta.modules.filter(m => m.tier === 'primary').sort((a, b) => (b.bounds.max[2] - b.bounds.min[2]) - (a.bounds.max[2] - a.bounds.min[2]))[0];
  const [mn, mx] = [hull.bounds.min, hull.bounds.max];
  const c = [0, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
  const len = mx[2] - mn[2], hei = mx[1] - mn[1];
  const d = Math.max(18, Math.min(60, len * 0.22));
  const eye = [mx[0] + d, c[1] + hei * 0.35, c[2] - d * 0.6], tgt = [mx[0] * 0.5, c[1], c[2] + d * 0.4];
  const imgs = [];
  for (const which of ['before', 'after'])
    imgs.push(await page.evaluate(([u, e, t]) => window.renderShip(u, { w: 560, h: 340, eye: e, target: t, fov: 45 }), [`/${which}/${name}.glb`, eye, tgt]));
  rows.push({ name, imgs });
}
await page.setViewportSize({ width: 1150, height: 100 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:14px system-ui;color:#c9d1d9;padding:8px"><div style="display:flex;gap:8px;margin-bottom:4px"><div style="width:560px">before</div><div>after</div></div>${rows.map(r => `<div style="display:flex;gap:8px;margin-bottom:8px">${r.imgs.map(i => `<img src="${i}" width=560>`).join('')}</div>`).join('')}</body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
