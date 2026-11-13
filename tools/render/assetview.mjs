// Shows that every part can be found by code: node assetview.mjs <dir> <name> <out.png>
// The ship as it comes, and the same ship in clay with parts picked out by their glTF extras "type".
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { join, extname, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const here = dirname(fileURLToPath(import.meta.url));
const [dirArg, name, out] = process.argv.slice(2);
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
const glb = `/data/${name}.glb`;
const colours = { turret: '#ff5a4f', hardpoint: '#ff5a4f', engine: '#ffa630', window: '#5fd6ff', bridge_block: '#b48cff', superstructure: '#b48cff', antenna: '#7dff8a', lattice_mast: '#7dff8a', radome: '#7dff8a', floodlight: '#fff27a', docking_collar: '#ff7ad9' };
const legend = [['turret / hardpoint', '#ff5a4f'], ['engine', '#ffa630'], ['window', '#5fd6ff'], ['bridge block', '#b48cff'], ['masts, radomes, antennas', '#7dff8a'], ['floodlight', '#fff27a'], ['docking collar', '#ff7ad9']];
const a = await page.evaluate(u => window.renderShip(u, { w: 800, h: 480, view: 'quarter' }), glb);
const b = await page.evaluate(([u, h]) => window.renderShip(u, { w: 800, h: 480, view: 'quarter', mode: 'clay', highlight: h }), [glb, colours]);
await page.setViewportSize({ width: 1640, height: 560 });
await page.setContent(`<body style="margin:0;background:#0d1117;font:15px system-ui;color:#c9d1d9">
<div style="display:flex;gap:16px;padding:16px 16px 8px"><div><img src="${a}" width=800><div>as generated</div></div>
<div><img src="${b}" width=800><div>found by <code>extras.type</code></div></div></div>
<div style="padding:0 16px;display:flex;gap:18px;flex-wrap:wrap">${legend.map(([t, c]) => `<span><span style="display:inline-block;width:12px;height:12px;background:${c};border-radius:2px;margin-right:6px"></span>${t}</span>`).join('')}</div></body>`);
await page.screenshot({ path: out, fullPage: true });
await browser.close(); server.close();
console.log('wrote', out);
