// Renders a fleet folder (from `badship fleet`) as a true-scale lineup: node lineup.mjs <dir> [--view side|low]
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const dir = resolve(args.find(a => !a.startsWith('--')));
const view = args.includes('--view') ? args[args.indexOf('--view') + 1] : 'low';
const index = JSON.parse(readFileSync(join(dir, 'index.json'), 'utf8'));
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
const items = [...index].sort((a, b) => a.length - b.length).map(s => ({
  url: `/data/${s.name}.glb`, label: `${s.archetype} · ${s.size}`, sub: (s.manufacturer ?? '').split(' ')[0] + ` · ${Math.round(s.length)} m`,
}));
const title = index[0]?.faction ?? '';
const img = await page.evaluate(([it, v, t]) => window.renderLineup(it, { view: v, title: t }), [items, view, title]);
writeFileSync(join(dir, 'lineup.png'), Buffer.from(img.split(',')[1], 'base64'));
console.log('wrote', join(dir, 'lineup.png'));
await browser.close(); server.close();
