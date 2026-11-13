// Renders single ships from several views: node closeup.mjs <dir> <name> [views...]
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name, ...viewsArg] = process.argv.slice(2);
const dir = resolve(dirArg);
const views = viewsArg.length ? viewsArg : ['quarter', 'rear', 'front', 'side'];
const types = { '.html': 'text/html', '.js': 'text/javascript', '.glb': 'model/gltf-binary' };
const server = createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/three/') ? join(here, 'node_modules/three', url.slice(7)) : url.startsWith('/data/') ? join(dir, url.slice(6)) : join(here, url === '/' ? 'page.html' : url.slice(1));
  if (!existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' }); res.end(readFileSync(file));
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage();
await page.goto(`http://127.0.0.1:${server.address().port}/`);
await page.waitForFunction(() => window.ready === true);
const imgs = [];
for (const v of views) imgs.push(await page.evaluate(([u, v]) => window.renderShip(u, { w: 1100, h: 620, view: v }), [`/data/${name}.glb`, v]));
await page.evaluate(([t, c]) => window.buildSheet(t, c, 2, 1100), [name, imgs.map((src, i) => ({ name: views[i], archetype: '', family: '', length: '', prow: '', status: 'pass', images: [src] }))]);
await (await page.$('#sheet')).screenshot({ path: join(dir, `${name}_closeup.png`) });
console.log('wrote', join(dir, `${name}_closeup.png`));
await browser.close(); server.close();
