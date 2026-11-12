// Renders one ship's LOD0, LOD1, LOD2 and collision side by side: node lods.mjs <dir> <name>
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name] = process.argv.slice(2);
const dir = resolve(dirArg);
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
const meta = JSON.parse(readFileSync(join(dir, `${name}.metadata.json`), 'utf8'));
const files = [['LOD0', '', meta.lods[0].triangles], ['LOD1', '.lod1', meta.lods[1].triangles], ['LOD2', '.lod2', meta.lods[2].triangles], ['Collision', '.collision', `${meta.collision.pieces} pieces`]];
const cells = [];
for (const [label, ext, tris] of files) {
  const img = await page.evaluate(([u]) => window.renderShip(u, { w: 1000, h: 560, view: 'quarter', mode: u.includes('collision') ? 'clay' : 'beauty' }), [`/data/${name}${ext}.glb`]);
  cells.push({ name: label, archetype: typeof tris === 'number' ? `${tris.toLocaleString('en-US')} triangles` : tris, family: '', length: '', prow: '', status: 'pass', images: [img] });
}
await page.evaluate(([c]) => window.buildSheet('Levels of detail and collision', c, 2, 1000), [cells]);
await (await page.$('#sheet')).screenshot({ path: join(dir, `${name}_lods.png`) });
console.log('wrote', join(dir, `${name}_lods.png`));
await browser.close(); server.close();
