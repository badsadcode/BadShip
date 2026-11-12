// Contact sheets of a converted kit: node kitview.mjs <dir with layer_*.glb>
// Writes <layer>_parts.png (every part alone) and <layer>_overview.png (the layer as laid out in the .blend).
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync, readdirSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const dir = resolve(process.argv[2]);
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : url.startsWith('/data/') ? join(dir, url.slice(6)) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': extname(file) === '.html' ? 'text/html' : extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
page.on('pageerror', e => console.error('page error:', e.message));
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
for (const f of readdirSync(dir).filter(f => /^layer_\d+_.*\.glb$/.test(f)).sort()) {
  const base = f.replace('.glb', '');
  const parts = await page.evaluate(u => window.renderParts(u), `/data/${f}`);
  if (parts.length === 0) continue;
  const cells = parts.map(p => ({ name: p.name, archetype: `${p.tris} tris`, family: '', length: '', prow: `${p.size.join(' × ')}`, status: 'pass', images: [p.img] }));
  await page.evaluate(([t, c]) => window.buildSheet(t, c, 6, 300), [`${base} — ${parts.length} parts`, cells]);
  await (await page.$('#sheet')).screenshot({ path: join(dir, `${base}_parts.png`) });
  const overview = await page.evaluate(u => window.renderShip(u, { w: 1600, h: 900, view: 'quarter' }), `/data/${f}`);
  writeFileSync(join(dir, `${base}_overview.png`), Buffer.from(overview.split(',')[1], 'base64'));
  console.log(base, parts.length, 'parts');
}
await browser.close(); server.close();
