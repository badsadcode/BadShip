// Builds the self-contained BadShip Hangar page (an interactive 3D gallery) from a gallery folder.
//
//   node build.mjs <galleryDir> <out.html>
//
// Every ship's .glb is embedded as base64 next to its recipe, metadata and validation summary, so the page
// needs nothing but three.js from a CDN.
import { readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const [dir, out] = process.argv.slice(2);
if (!dir || !out) { console.error('usage: node build.mjs <galleryDir> <out.html>'); process.exit(1); }

const index = JSON.parse(readFileSync(join(dir, 'index.json'), 'utf8'));
const fleet = index.map(s => {
  const read = ext => JSON.parse(readFileSync(join(dir, `${s.name}.${ext}.json`), 'utf8'));
  const meta = read('metadata');
  const recipe = read('recipe');
  const validation = read('validation');
  const markers = {};
  for (const m of meta.markers) markers[m.kind] = (markers[m.kind] ?? 0) + 1;
  const modules = {};
  for (const m of meta.modules) modules[m.tier] = (modules[m.tier] ?? 0) + 1;
  return {
    name: s.name, seed: s.seed, archetype: s.archetype, family: s.family,
    status: validation.status, triangles: meta.triangles, size: meta.bounds.size,
    plan: recipe.resolved, markers, modules,
    glb: readFileSync(join(dir, `${s.name}.glb`)).toString('base64'),
  };
});

// Keep "</script" out of the embedded JSON.
const json = JSON.stringify(fleet).replaceAll('</', '<\\/');
const html = readFileSync(join(here, 'hangar.template.html'), 'utf8').replace('/*__FLEET__*/', () => json);
writeFileSync(out, html);
console.log(`wrote ${out} (${(html.length / 1048576).toFixed(1)} MB, ${fleet.length} ships)`);
